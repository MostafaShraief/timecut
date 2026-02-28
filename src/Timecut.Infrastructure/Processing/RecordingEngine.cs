using Microsoft.Extensions.Logging;
using Timecut.Core.Interfaces;
using Timecut.Core.Models;
using Timecut.Infrastructure.FFmpeg;
using Timecut.Infrastructure.Playwright;

namespace Timecut.Infrastructure.Processing;

/// <summary>
/// Orchestrates the full recording pipeline: Playwright captures frames → FFmpeg encodes video.
/// Implements the IRecordingEngine interface from Core.
/// </summary>
public class RecordingEngine : IRecordingEngine
{
    private readonly PlaywrightService _playwrightService;
    private readonly FFmpegService _ffmpegService;
    private readonly IFileManager _fileManager;
    private readonly ILogger<RecordingEngine> _logger;

    public RecordingEngine(
        PlaywrightService playwrightService,
        FFmpegService ffmpegService,
        IFileManager fileManager,
        ILogger<RecordingEngine> logger)
    {
        _playwrightService = playwrightService;
        _ffmpegService = ffmpegService;
        _fileManager = fileManager;
        _logger = logger;
    }

    public async Task ProcessJobAsync(
        RecordingJob job,
        IProgress<(double percent, string message)> progress,
        CancellationToken cancellationToken)
    {
        _fileManager.EnsureOutputDirectory();
        var outputPath = _fileManager.GetOutputPath(job.Id, job.Options.OutputFileName);
        job.OutputPath = outputPath;

        _logger.LogInformation("Starting recording for job {JobId}: {Source} → {Output}",
            job.Id, job.SourceName, outputPath);

        progress.Report((0, "Starting FFmpeg encoder..."));

        await using var encoder = _ffmpegService.StartEncoding(job.Options, outputPath);

        try
        {
            await _playwrightService.CaptureFramesAsync(
                job,
                async frameBytes => await encoder.WriteFrameAsync(frameBytes),
                progress,
                cancellationToken);

            progress.Report((95, "Finalizing video encoding..."));
            var success = await encoder.FinishAsync(cancellationToken);

            if (!success)
            {
                throw new InvalidOperationException("FFmpeg encoding failed. Check logs for details.");
            }

            if (File.Exists(outputPath))
            {
                job.OutputFileSize = new FileInfo(outputPath).Length;
                _logger.LogInformation("Recording complete: {OutputPath} ({Size} bytes)",
                    outputPath, job.OutputFileSize);
            }

            progress.Report((100, "Recording complete"));
        }
        catch
        {
            // Clean up partial output on failure
            try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
            encoder.Kill();
            throw;
        }
    }
}
