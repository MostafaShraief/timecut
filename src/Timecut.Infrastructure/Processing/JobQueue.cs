using System.Collections.Concurrent;
using System.Threading.Channels;
using Timecut.Core.Interfaces;
using Timecut.Core.Models;

namespace Timecut.Infrastructure.Processing;

/// <summary>
/// Thread-safe job queue backed by Channel&lt;T&gt; for async producer/consumer 
/// and ConcurrentDictionary for job lookups.
/// </summary>
public class JobQueue : IJobQueue
{
    private readonly ConcurrentDictionary<string, RecordingJob> _jobs = new();
    private readonly Channel<RecordingJob> _channel = Channel.CreateUnbounded<RecordingJob>();

    public async Task EnqueueAsync(RecordingJob job)
    {
        _jobs[job.Id] = job;
        await _channel.Writer.WriteAsync(job);
    }

    public async Task<RecordingJob> DequeueAsync(CancellationToken cancellationToken)
    {
        return await _channel.Reader.ReadAsync(cancellationToken);
    }

    public RecordingJob? GetJob(string jobId)
    {
        _jobs.TryGetValue(jobId, out var job);
        return job;
    }

    public IReadOnlyList<RecordingJob> GetAllJobs()
    {
        return _jobs.Values
            .OrderByDescending(j => j.CreatedAt)
            .ToList()
            .AsReadOnly();
    }

    public bool RemoveJob(string jobId)
    {
        return _jobs.TryRemove(jobId, out _);
    }
}
