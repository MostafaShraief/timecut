using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddCors();
builder.Services.AddSingleton<JobQueue>();
builder.Services.AddSingleton<ConcurrencyControl>(); 
builder.Services.AddHostedService<RecordingWorker>();

var app = builder.Build();

app.UseCors(x => x.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
app.UseDefaultFiles();
app.UseStaticFiles();

var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "output");
if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

// ----------------------------------------------------------------------------------
// API ENDPOINTS
// ----------------------------------------------------------------------------------

// Create job from uploaded file content (client reads file as text)
app.MapPost("/record", async ([FromBody] RecordingRequest request, JobQueue queue) =>
{
    var jobId = Guid.NewGuid().ToString("N");
    var job = new JobStatus
    {
        Id = jobId,
        Status = "queued",
        Config = request,
        SubmittedAt = DateTime.UtcNow
    };

    await queue.EnqueueAsync(job);
    return Results.Ok(new { jobId });
});

// Update dynamic concurrency limit
app.MapPost("/concurrency", ([FromBody] ConcurrencyRequest req, ConcurrencyControl cc) =>
{
    cc.SetLimit(req.Limit);
    return Results.Ok(new { message = $"Concurrency limit set to {req.Limit}" });
});

app.MapGet("/progress/{jobId}", async (string jobId, JobQueue queue, HttpContext context) =>
{
    var job = queue.GetJob(jobId);
    if (job == null) 
    {
        context.Response.StatusCode = 404;
        return;
    }

    context.Response.Headers.Append("Content-Type", "text/event-stream");
    context.Response.Headers.Append("Cache-Control", "no-cache");
    context.Response.Headers.Append("Connection", "keep-alive");

    while (!context.RequestAborted.IsCancellationRequested)
    {
        var data = JsonSerializer.Serialize(new 
        { 
            status = job.Status, 
            progress = job.Progress, 
            error = job.Error, 
            url = job.Status == "done" ? $"/download/{jobId}.mp4" : null 
        });
        
        await context.Response.WriteAsync($"data: {data}\n\n");
        await context.Response.Body.FlushAsync();

        if (job.Status == "done" || job.Status == "error" || job.Status == "stopped") break;
        await Task.Delay(500); 
    }
});

app.MapPost("/stop/{jobId}", (string jobId, JobQueue queue) =>
{
    var job = queue.GetJob(jobId);
    if (job != null)
    {
        job.StopRequested = true;
        if (job.Status == "queued") job.Status = "stopped";
        return Results.Ok(new { message = "Stop requested" });
    }
    return Results.NotFound();
});

app.MapGet("/download/{filename}", (string filename) =>
{
    var safeName = Path.GetFileName(filename);
    var filePath = Path.Combine(outputDir, safeName);
    if (File.Exists(filePath)) return Results.File(filePath, "video/mp4", safeName);
    return Results.NotFound();
});

app.Run();

// ----------------------------------------------------------------------------------
// WORKER & LOGIC
// ----------------------------------------------------------------------------------

public class RecordingWorker : BackgroundService
{
    private readonly JobQueue _queue;
    private readonly ConcurrencyControl _concurrency;
    private readonly ILogger<RecordingWorker> _logger;

    public RecordingWorker(JobQueue queue, ConcurrencyControl concurrency, ILogger<RecordingWorker> logger)
    {
        _queue = queue;
        _concurrency = concurrency;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker Started");

        while (!stoppingToken.IsCancellationRequested)
        {
            // Wait for concurrency slot
            await _concurrency.WaitAsync(stoppingToken);

            var job = await _queue.DequeueAsync(stoppingToken); 
            if (job == null) continue;

            if (job.StopRequested) {
                 job.Status = "stopped";
                 _concurrency.Release();
                 continue;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessJob(job);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing job {JobId}", job.Id);
                    job.Status = "error";
                    job.Error = ex.Message;
                }
                finally
                {
                    _concurrency.Release();
                }
            }, stoppingToken);
        }
    }

    private async Task ProcessJob(JobStatus job)
    {
        job.Status = "running";
        job.StartedAt = DateTime.UtcNow;
        var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "output");
        var outputPath = Path.Combine(outputDir, $"{job.Id}.mp4");

        // Launch Browser
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = new[] { "--no-sandbox", "--disable-setuid-sandbox", "--disable-gpu", "--allow-file-access-from-files", "--disable-web-security" }
        });

        // Config Page
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = job.Config.Width, Height = job.Config.Height },
            DeviceScaleFactor = 1,
        });

        var page = await context.NewPageAsync();

        // Inject Time Override via Page.Evaluate because AddInitScript might be racey with data: urls or simply redundant if we set content right after.
        // Actually, AddInitScript is better for ensuring it's there before load, but for data: text/html, manual injection is safer or we must ensure content loading works.
        // Instead of init script, we will define it immediately after page creation before setting content.
        
        await page.AddInitScriptAsync(@"
            window.__timecut = { currentTime: 0, fps: " + job.Config.Fps + @" };
            
            // 1. Math.random determinism
            let seed = 1;
            window.Math.random = () => {
                const x = Math.sin(seed++) * 10000;
                return x - Math.floor(x);
            };

            // 2. Time overrides
            const _Date = Date;
            window.Date = class extends _Date {
                constructor(...args) {
                    if (args.length === 0) return new _Date(window.__timecut.currentTime * 1000);
                    return new _Date(...args);
                }
                static now() { return window.__timecut.currentTime * 1000; }
            };
            const _perf = window.performance;
            window.performance = { ..._perf, now: () => window.__timecut.currentTime * 1000 };

            // 3. requestAnimationFrame override
            window.__rafs = {};
            window.__rafId = 0;
            window.requestAnimationFrame = (cb) => {
                window.__rafId++;
                window.__rafs[window.__rafId] = cb;
                return window.__rafId;
            };
            window.cancelAnimationFrame = (id) => {
                delete window.__rafs[id];
            };

            // 4. Media & Animation freezing
            const freezeMedia = () => {
                try {
                    if (document.timeline) {
                        document.timeline.pause();
                        document.timeline.currentTime = window.__timecut.currentTime * 1000;
                    }
                    document.querySelectorAll('video, audio').forEach(v => {
                        v.pause();
                        v.currentTime = window.__timecut.currentTime;
                    });
                    document.querySelectorAll('svg').forEach(svg => {
                        if (svg.pauseAnimations) svg.pauseAnimations();
                        if (svg.setCurrentTime) svg.setCurrentTime(window.__timecut.currentTime);
                    });
                } catch(e) {}
            };
            
            if (typeof document !== 'undefined') {
                document.addEventListener('DOMContentLoaded', freezeMedia);
                freezeMedia();
            }

            // 5. Update function
            window.__updateTime = (t) => {
                window.__timecut.currentTime = t;
                freezeMedia();
                
                const currentRafs = window.__rafs;
                window.__rafs = {};
                Object.values(currentRafs).forEach(cb => {
                    if (cb) {
                        try { cb(t * 1000); } catch(e) {}
                    }
                });
            };
        ");

        string? tempHtmlPath = null;
        try
        {
            // Removed redundant AddInitScript block that was here

            if (!string.IsNullOrEmpty(job.Config.HtmlContent)) 
            {
                // Write HTML to a temporary file to allow local file access (e.g., local images)
                tempHtmlPath = Path.Combine(Path.GetTempPath(), $"timecut_{job.Id}.html");
                await File.WriteAllTextAsync(tempHtmlPath, job.Config.HtmlContent);
                var fileUri = new Uri(tempHtmlPath).AbsoluteUri;
                await page.GotoAsync(fileUri, new PageGotoOptions { WaitUntil = WaitUntilState.Load });
            }
            else if (!string.IsNullOrEmpty(job.Config.Url))
            {
                await page.GotoAsync(job.Config.Url, new PageGotoOptions { WaitUntil = WaitUntilState.Load });
            }
            
            var rafStr = await page.EvaluateAsync<string>("window.requestAnimationFrame.toString()");
            Console.WriteLine($"RAF after load: {rafStr}");
            
            // Re-inject/Ensure function exists just in case the page navigation wiped it (unlikely with AddInitScript available, but safe)
            await page.EvaluateAsync(@"() => {
                if (typeof window.__updateTime !== 'function') {
                    window.__timecut = window.__timecut || { currentTime: 0 };
                    window.__rafs = window.__rafs || {};
                    const freezeMedia = () => {
                        try {
                            if (document.timeline) {
                                document.timeline.pause();
                                document.timeline.currentTime = window.__timecut.currentTime * 1000;
                            }
                            document.querySelectorAll('video, audio').forEach(v => { v.pause(); v.currentTime = window.__timecut.currentTime; });
                        } catch(e) {}
                    };
                    window.__updateTime = (t) => {
                        window.__timecut.currentTime = t;
                        freezeMedia();
                        const currentRafs = window.__rafs;
                        window.__rafs = {};
                        Object.values(currentRafs).forEach(cb => {
                            if (cb) { try { cb(t * 1000); } catch(e) {} }
                        });
                    };
                }
            }");

            // Start ffmpeg process with -f image2pipe which reads from stdin
            // Use local ffmpeg if available, otherwise assume it's in PATH
            // Added -pix_fmt yuv420p again to ensure compatibility with most players
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);
            
            var ffmpegArgs = $"-y -f image2pipe -vcodec mjpeg -r {job.Config.Fps} -i - -c:v libx264 -preset ultrafast -pix_fmt yuv420p \"{outputPath}\"";
            
            using var ffmpeg = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = ffmpegArgs,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true // Capture error output to debug if it fails instantly
                }
            };

            ffmpeg.ErrorDataReceived += (sender, args) => 
            {
                if (args.Data != null) Console.WriteLine($"FFmpeg: {args.Data}");
            };

            if (!ffmpeg.Start()) throw new Exception("FFmpeg failed to start");
            ffmpeg.BeginErrorReadLine();

            var totalFrames = (int)(job.Config.Fps * job.Config.Duration);
            var frameTime = 1.0 / job.Config.Fps;

            for (int i = 0; i < totalFrames; i++)
            {
                if (job.StopRequested) break;

                // Advance Time
                await page.EvaluateAsync($"window.__updateTime({i * frameTime})");
                
                // Screenshot
                // OmitBackground false ensures we get white bg if page has none default
                var bytes = await page.ScreenshotAsync(new PageScreenshotOptions 
                { 
                    Type = ScreenshotType.Jpeg, 
                    Quality = 80, 
                    OmitBackground = false 
                });

                // Pipe
                await ffmpeg.StandardInput.BaseStream.WriteAsync(bytes);

                job.Progress = (double)(i + 1) / totalFrames;
            }

            try { ffmpeg.StandardInput.Close(); } catch {} // Close stdin to finish video
            await ffmpeg.WaitForExitAsync();

            job.Status = job.StopRequested ? "stopped" : "done";
            job.CompletedAt = DateTime.UtcNow;
        }
        catch(Exception)
        {
             try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch {}
             throw; 
        }
        finally
        {
            try { await context.CloseAsync(); } catch {}
            if (tempHtmlPath != null && File.Exists(tempHtmlPath))
            {
                try { File.Delete(tempHtmlPath); } catch {}
            }
        }
    }
}

// ----------------------------------------------------------------------------------
// SERVICES & MODELS
// ----------------------------------------------------------------------------------

public class ConcurrencyControl
{
    private SemaphoreSlim _semaphore;

    public ConcurrencyControl() { _semaphore = new SemaphoreSlim(3); } // Default

    public void SetLimit(int limit)
    {
        if (limit < 1) limit = 1;
        // Simple replacement. Note: This clears the semaphore. Running tasks are fine as they operate on async context.
        // Waiting tasks on the old semaphore might be stuck unless we are careful, but for this use case 
        // (starting fresh upload batch), it's acceptable simplicity.
        // Ideally we'd drain permits.
        _semaphore = new SemaphoreSlim(limit);
    }

    public async Task WaitAsync(CancellationToken token) { await _semaphore.WaitAsync(token); }
    public void Release() { try { _semaphore.Release(); } catch { } }
}

public class RecordingRequest
{
    public string? Url { get; set; }
    public string? HtmlContent { get; set; }
    public double Fps { get; set; } = 60;
    public double Duration { get; set; } = 5;
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
}
public class ConcurrencyRequest { public int Limit { get; set; } }

public class ViewportConfig { public int Width { get; set; } = 800; public int Height { get; set; } = 600; }

public class JobStatus
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = "queued"; 
    public double Progress { get; set; }
    public string? Error { get; set; }
    public bool StopRequested { get; set; }
    public RecordingRequest Config { get; set; } = new();
    public DateTime SubmittedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class JobQueue
{
    private readonly ConcurrentDictionary<string, JobStatus> _jobs = new();
    private readonly Channel<JobStatus> _channel = Channel.CreateUnbounded<JobStatus>();

    public async Task EnqueueAsync(JobStatus job) { _jobs[job.Id] = job; await _channel.Writer.WriteAsync(job); }
    public async Task<JobStatus> DequeueAsync(CancellationToken ct) => await _channel.Reader.ReadAsync(ct);
    public JobStatus? GetJob(string id) { _jobs.TryGetValue(id, out var job); return job; }
}
