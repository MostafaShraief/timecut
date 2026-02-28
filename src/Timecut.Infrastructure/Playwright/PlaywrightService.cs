using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Timecut.Core.Interfaces;
using Timecut.Core.Models;

namespace Timecut.Infrastructure.Playwright;

/// <summary>
/// Handles the Playwright page setup, time-override injection, navigation,
/// and frame-by-frame screenshot capture pipeline.
/// </summary>
public class PlaywrightService
{
    private readonly BrowserManager _browserManager;
    private readonly IFileManager _fileManager;
    private readonly ILogger<PlaywrightService> _logger;
    private readonly string _timeOverrideScript;

    public PlaywrightService(
        BrowserManager browserManager,
        IFileManager fileManager,
        ILogger<PlaywrightService> logger)
    {
        _browserManager = browserManager;
        _fileManager = fileManager;
        _logger = logger;
        _timeOverrideScript = LoadTimeOverrideScript();
    }

    private static string LoadTimeOverrideScript()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "Timecut.Infrastructure.Playwright.Scripts.timeOverride.js";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Captures frames from an HTML page and streams JPEG/PNG bytes via the provided callback.
    /// </summary>
    public async Task CaptureFramesAsync(
        RecordingJob job,
        Func<byte[], Task> onFrame,
        IProgress<(double percent, string message)> progress,
        CancellationToken cancellationToken)
    {
        var options = job.Options;
        var context = await _browserManager.CreateContextAsync(options);
        string? tempFilePath = null;

        try
        {
            var page = await context.NewPageAsync();

            // Inject time override script with configured FPS
            var script = _timeOverrideScript.Replace(
                "window.__timecut = { currentTime: 0, fps: 30 };",
                $"window.__timecut = {{ currentTime: 0, fps: {options.Fps} }};");
            await page.AddInitScriptAsync(script);

            // Navigate to content
            if (job.SourceType == JobSourceType.InlineHtml && !string.IsNullOrEmpty(job.HtmlContent))
            {
                tempFilePath = _fileManager.CreateTempHtmlFile(job.HtmlContent, job.Id);
                var fileUri = new Uri(tempFilePath).AbsoluteUri;
                await page.GotoAsync(fileUri, new PageGotoOptions { WaitUntil = WaitUntilState.Load });
            }
            else if (job.SourceType == JobSourceType.FilePath && !string.IsNullOrEmpty(job.FilePath))
            {
                var fileUri = new Uri(job.FilePath).AbsoluteUri;
                await page.GotoAsync(fileUri, new PageGotoOptions { WaitUntil = WaitUntilState.Load });
            }
            else if (job.SourceType == JobSourceType.Url && !string.IsNullOrEmpty(job.Url))
            {
                await page.GotoAsync(job.Url, new PageGotoOptions { WaitUntil = WaitUntilState.Load });
            }
            else
            {
                throw new InvalidOperationException("No valid source provided for recording.");
            }

            // Apply page zoom using CSS zoom (works across all content types)
            if (Math.Abs(options.PageZoom - 1.0) > 0.01)
            {
                await page.EvaluateAsync($"document.documentElement.style.zoom = '{options.PageZoom}'");
            }

            // Ensure time override script loaded properly
            await page.EvaluateAsync(@"() => {
                if (typeof window.__updateTime !== 'function') {
                    console.warn('Timecut: __updateTime not found after load, attempting re-injection');
                }
            }");

            // Wait a bit for initial render to settle
            await page.WaitForTimeoutAsync(100);

            // Calculate frame parameters
            var totalFrames = (int)(options.Fps * options.Duration);
            var frameTime = 1.0 / options.Fps;
            var startFrame = (int)(options.StartOffset * options.Fps);

            var screenshotOptions = new PageScreenshotOptions
            {
                Type = options.ScreenshotFormat == ScreenshotFormat.Png
                    ? ScreenshotType.Png
                    : ScreenshotType.Jpeg,
                Quality = options.ScreenshotFormat == ScreenshotFormat.Jpeg
                    ? options.ScreenshotQuality
                    : null,
                OmitBackground = false
            };

            _logger.LogInformation("Starting frame capture: {Total} frames at {Fps} FPS for job {JobId}",
                totalFrames, options.Fps, job.Id);

            // Frame capture loop
            for (int i = 0; i < totalFrames; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (job.StopRequested) break;

                var currentTime = (startFrame + i) * frameTime;
                await page.EvaluateAsync($"window.__updateTime({currentTime})");

                var bytes = await page.ScreenshotAsync(screenshotOptions);
                await onFrame(bytes);

                var percent = (double)(i + 1) / totalFrames * 100;
                progress.Report((percent, $"Capturing frame {i + 1}/{totalFrames}"));
            }

            _logger.LogInformation("Frame capture complete for job {JobId}", job.Id);
        }
        finally
        {
            try { await context.CloseAsync(); } catch { }
            if (tempFilePath != null)
                _fileManager.CleanupTempFile(tempFilePath);
        }
    }
}
