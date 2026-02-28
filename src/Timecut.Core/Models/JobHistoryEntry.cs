namespace Timecut.Core.Models;

public class JobHistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SourceName { get; set; } = string.Empty;
    public JobSourceType SourceType { get; set; }
    public string? SourceContent { get; set; }
    public string? SourceUrl { get; set; }
    public bool UsedTemplate { get; set; }
    public string? TemplateId { get; set; }
    public string? TemplateName { get; set; }

    // Options snapshot
    public double Fps { get; set; }
    public double Duration { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double PageZoom { get; set; }

    // Result
    public JobStatusEnum FinalStatus { get; set; }
    public string? OutputPath { get; set; }
    public long? OutputFileSize { get; set; }
    public string? ErrorMessage { get; set; }

    // Timestamps
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public TimeSpan? ProcessingTime { get; set; }
}
