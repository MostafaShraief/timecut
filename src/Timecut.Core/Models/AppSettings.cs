namespace Timecut.Core.Models;

public class AppSettings
{
    public string OutputDirectory { get; set; } = string.Empty;
    public string? FfmpegPath { get; set; }
    public int MaxConcurrentJobs { get; set; } = 3;
    public bool MinimizeToTray { get; set; } = true;
    public bool AutoCleanupEnabled { get; set; }
    public int AutoCleanupDays { get; set; } = 30;
    public string Theme { get; set; } = "Dark";
}
