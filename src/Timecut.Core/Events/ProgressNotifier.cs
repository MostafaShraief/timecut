using Timecut.Core.Models;

namespace Timecut.Core.Events;

/// <summary>
/// In-process event bus for real-time progress updates from the recording engine to UI components.
/// Replaces the old broken SSE mechanism with zero-latency direct events.
/// </summary>
public class ProgressNotifier
{
    /// <summary>
    /// Fired when a job's progress updates. Parameters: jobId, percent (0-100), statusMessage.
    /// </summary>
    public event Action<string, double, string>? OnProgress;

    /// <summary>
    /// Fired when a job status changes (queued, running, done, error, cancelled).
    /// </summary>
    public event Action<string, JobStatusEnum, string?>? OnStatusChanged;

    /// <summary>
    /// Fired when a job completes successfully. Parameter: jobId, outputPath.
    /// </summary>
    public event Action<string, string>? OnJobCompleted;

    /// <summary>
    /// Fired when a job encounters an error. Parameter: jobId, errorMessage.
    /// </summary>
    public event Action<string, string>? OnJobError;

    /// <summary>
    /// Fired when the active job list changes (add/remove).
    /// </summary>
    public event Action? OnJobListChanged;

    public void ReportProgress(string jobId, double percent, string message)
    {
        OnProgress?.Invoke(jobId, percent, message);
    }

    public void ReportStatusChange(string jobId, JobStatusEnum status, string? error = null)
    {
        OnStatusChanged?.Invoke(jobId, status, error);
    }

    public void ReportJobCompleted(string jobId, string outputPath)
    {
        OnJobCompleted?.Invoke(jobId, outputPath);
    }

    public void ReportJobError(string jobId, string errorMessage)
    {
        OnJobError?.Invoke(jobId, errorMessage);
    }

    public void NotifyJobListChanged()
    {
        OnJobListChanged?.Invoke();
    }
}
