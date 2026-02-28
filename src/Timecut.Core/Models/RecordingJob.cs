namespace Timecut.Core.Models;

public class RecordingJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public JobStatusEnum Status { get; set; } = JobStatusEnum.Queued;
    public double Progress { get; set; }
    public string? StatusMessage { get; set; }
    public string? Error { get; set; }

    // Source
    public JobSourceType SourceType { get; set; }
    public string? Url { get; set; }
    public string? FilePath { get; set; }
    public string? HtmlContent { get; set; }
    public string SourceName { get; set; } = "Untitled";

    // Template
    public bool UseTemplate { get; set; }
    public string? TemplateId { get; set; }

    // Options
    public RecordingOptions Options { get; set; } = new();

    // Output
    public string? OutputPath { get; set; }
    public long? OutputFileSize { get; set; }

    // Timestamps
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // Cancellation
    public bool StopRequested { get; set; }
}

public enum JobStatusEnum
{
    Queued,
    Running,
    Done,
    Error,
    Cancelled
}

public enum JobSourceType
{
    Url,
    FilePath,
    InlineHtml
}
