using Timecut.Core.Models;

namespace Timecut.Core.Interfaces;

public interface IFileManager
{
    string GetOutputDirectory();
    void SetOutputDirectory(string path);
    string CreateTempHtmlFile(string htmlContent, string jobId);
    string ComposeHtml(HtmlTemplate template, string contentHtml);
    string CreateComposedTempFile(HtmlTemplate template, string contentHtml, string jobId);
    void CleanupTempFile(string? filePath);
    bool OutputFileExists(string path);
    long GetFileSize(string path);
    void EnsureOutputDirectory();
    string GetOutputPath(string jobId, string? customFileName = null);
    string? FindFfmpeg();
}
