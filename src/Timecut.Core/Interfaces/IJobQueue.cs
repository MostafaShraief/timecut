using Timecut.Core.Models;

namespace Timecut.Core.Interfaces;

public interface IJobQueue
{
    Task EnqueueAsync(RecordingJob job);
    Task<RecordingJob> DequeueAsync(CancellationToken cancellationToken);
    RecordingJob? GetJob(string jobId);
    IReadOnlyList<RecordingJob> GetAllJobs();
    bool RemoveJob(string jobId);
}
