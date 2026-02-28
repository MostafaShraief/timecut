using Timecut.Core.Interfaces;
using Timecut.Core.Models;

namespace Timecut.Infrastructure.FileManagement;

/// <summary>
/// Handles file operations: temp file creation, output directory management,
/// HTML template composition, and FFmpeg discovery.
/// </summary>
public class FileManager : IFileManager
{
    private string _outputDirectory;

    public FileManager()
    {
        _outputDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "Timecut");
    }

    public string GetOutputDirectory() => _outputDirectory;

    public void SetOutputDirectory(string path)
    {
        _outputDirectory = path;
        EnsureOutputDirectory();
    }

    public void EnsureOutputDirectory()
    {
        if (!Directory.Exists(_outputDirectory))
            Directory.CreateDirectory(_outputDirectory);
    }

    public string GetOutputPath(string jobId, string? customFileName = null)
    {
        EnsureOutputDirectory();
        var fileName = string.IsNullOrWhiteSpace(customFileName)
            ? $"{jobId}.mp4"
            : customFileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                ? customFileName
                : $"{customFileName}.mp4";

        // Sanitize filename
        foreach (var c in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(c, '_');

        return Path.Combine(_outputDirectory, fileName);
    }

    public string CreateTempHtmlFile(string htmlContent, string jobId)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"timecut_{jobId}.html");
        File.WriteAllText(tempPath, htmlContent);
        return tempPath;
    }

    public string ComposeHtml(HtmlTemplate template, string contentHtml)
    {
        if (string.IsNullOrEmpty(template.Content))
            return contentHtml;

        if (template.Content.Contains(HtmlTemplate.ContentPlaceholder))
        {
            return template.Content.Replace(HtmlTemplate.ContentPlaceholder, contentHtml);
        }

        // Fallback: inject before </body> if placeholder not found
        var bodyCloseIndex = template.Content.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (bodyCloseIndex >= 0)
        {
            return template.Content.Insert(bodyCloseIndex, contentHtml);
        }

        // Last resort: append content
        return template.Content + contentHtml;
    }

    public string CreateComposedTempFile(HtmlTemplate template, string contentHtml, string jobId)
    {
        var composed = ComposeHtml(template, contentHtml);
        return CreateTempHtmlFile(composed, jobId);
    }

    public void CleanupTempFile(string? filePath)
    {
        if (filePath != null && File.Exists(filePath))
        {
            try { File.Delete(filePath); } catch { }
        }
    }

    public bool OutputFileExists(string path) => File.Exists(path);

    public long GetFileSize(string path)
    {
        return File.Exists(path) ? new FileInfo(path).Length : 0;
    }

    public string? FindFfmpeg()
    {
        // Check common locations
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"),
            Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe"),
            @"C:\ffmpeg\bin\ffmpeg.exe",
            @"C:\ProgramData\chocolatey\bin\ffmpeg.exe"
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        // Check PATH
        var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(';') ?? Array.Empty<string>();
        foreach (var dir in pathDirs)
        {
            var ffmpegPath = Path.Combine(dir.Trim(), "ffmpeg.exe");
            if (File.Exists(ffmpegPath))
                return ffmpegPath;
        }

        return null; // Will fall back to just "ffmpeg" and hope it's in PATH
    }
}
