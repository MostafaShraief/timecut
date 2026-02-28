using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Timecut.Core.Interfaces;
using Timecut.Core.Models;

namespace Timecut.Infrastructure.FFmpeg;

/// <summary>
/// Manages FFmpeg process lifecycle for encoding frames into MP4 video.
/// </summary>
public class FFmpegService
{
    private readonly IFileManager _fileManager;
    private readonly ILogger<FFmpegService> _logger;

    public FFmpegService(IFileManager fileManager, ILogger<FFmpegService> logger)
    {
        _fileManager = fileManager;
        _logger = logger;
    }

    /// <summary>
    /// Starts an FFmpeg encoding process that accepts frames piped to stdin.
    /// Returns a wrapper that allows writing frames and completing the encoding.
    /// </summary>
    public FFmpegEncoder StartEncoding(RecordingOptions options, string outputPath)
    {
        var ffmpegPath = _fileManager.FindFfmpeg() ?? "ffmpeg";
        var inputFormat = options.ScreenshotFormat == ScreenshotFormat.Png ? "png" : "mjpeg";
        var inputCodec = options.ScreenshotFormat == ScreenshotFormat.Png ? "png" : "mjpeg";

        var args = $"-y -f image2pipe -vcodec {inputCodec} -r {options.Fps} -i - " +
                   $"-c:v {options.VideoCodec} -preset {options.EncodingPreset} " +
                   $"-pix_fmt {options.PixelFormat} \"{outputPath}\"";

        _logger.LogInformation("Starting FFmpeg: {Path} {Args}", ffmpegPath, args);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = args,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        var errorOutput = new List<string>();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                errorOutput.Add(e.Data);
                _logger.LogTrace("FFmpeg: {Line}", e.Data);
            }
        };

        if (!process.Start())
            throw new InvalidOperationException("FFmpeg failed to start. Ensure FFmpeg is installed and accessible.");

        process.BeginErrorReadLine();

        return new FFmpegEncoder(process, errorOutput, _logger);
    }

    /// <summary>
    /// Checks if FFmpeg is available on the system.
    /// </summary>
    public bool IsAvailable()
    {
        try
        {
            var ffmpegPath = _fileManager.FindFfmpeg() ?? "ffmpeg";
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Wraps an active FFmpeg encoding process, providing methods to pipe frames and finalize.
/// </summary>
public class FFmpegEncoder : IAsyncDisposable
{
    private readonly Process _process;
    private readonly List<string> _errorOutput;
    private readonly ILogger _logger;
    private bool _disposed;

    internal FFmpegEncoder(Process process, List<string> errorOutput, ILogger logger)
    {
        _process = process;
        _errorOutput = errorOutput;
        _logger = logger;
    }

    public async Task WriteFrameAsync(byte[] frameBytes)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FFmpegEncoder));
        await _process.StandardInput.BaseStream.WriteAsync(frameBytes);
    }

    public async Task<bool> FinishAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return false;

        try
        {
            _process.StandardInput.Close();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing FFmpeg stdin");
        }

        try
        {
            await _process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("FFmpeg wait cancelled, killing process");
            try { _process.Kill(); } catch { }
            return false;
        }

        if (_process.ExitCode != 0)
        {
            var errors = string.Join(Environment.NewLine, _errorOutput.TakeLast(10));
            _logger.LogError("FFmpeg exited with code {ExitCode}. Last errors:\n{Errors}",
                _process.ExitCode, errors);
            return false;
        }

        _logger.LogInformation("FFmpeg encoding completed successfully");
        return true;
    }

    public void Kill()
    {
        try { _process.Kill(); } catch { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill();
                await _process.WaitForExitAsync();
            }
        }
        catch { }

        _process.Dispose();
    }
}
