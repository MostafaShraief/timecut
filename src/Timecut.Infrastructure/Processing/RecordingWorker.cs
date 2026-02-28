using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Timecut.Core.Events;
using Timecut.Core.Interfaces;
using Timecut.Core.Models;

namespace Timecut.Infrastructure.Processing;

/// <summary>
/// Background service that continuously dequeues recording jobs and processes them.
/// Reports real-time progress via ProgressNotifier (in-process event bus).
/// </summary>
public class RecordingWorker : BackgroundService
{
    private readonly IJobQueue _queue;
    private readonly ConcurrencyControl _concurrency;
    private readonly IRecordingEngine _engine;
    private readonly ProgressNotifier _notifier;
    private readonly IJobRepository _jobRepository;
    private readonly ILogger<RecordingWorker> _logger;

    public RecordingWorker(
        IJobQueue queue,
        ConcurrencyControl concurrency,
        IRecordingEngine engine,
        ProgressNotifier notifier,
        IJobRepository jobRepository,
        ILogger<RecordingWorker> logger)
    {
        _queue = queue;
        _concurrency = concurrency;
        _engine = engine;
        _notifier = notifier;
        _jobRepository = jobRepository;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Recording worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _concurrency.WaitAsync(stoppingToken);
                var job = await _queue.DequeueAsync(stoppingToken);

                if (job.StopRequested)
                {
                    job.Status = JobStatusEnum.Cancelled;
                    _notifier.ReportStatusChange(job.Id, JobStatusEnum.Cancelled);
                    _concurrency.Release();
                    continue;
                }

                _ = Task.Run(async () => await ProcessJobAsync(job, stoppingToken), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in recording worker loop");
                _concurrency.Release();
            }
        }

        _logger.LogInformation("Recording worker stopped");
    }

    private async Task ProcessJobAsync(RecordingJob job, CancellationToken appStopping)
    {
        try
        {
            job.Status = JobStatusEnum.Running;
            job.StartedAt = DateTime.UtcNow;
            _notifier.ReportStatusChange(job.Id, JobStatusEnum.Running);

            using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(appStopping);

            var progress = new Progress<(double percent, string message)>(p =>
            {
                job.Progress = p.percent;
                job.StatusMessage = p.message;
                _notifier.ReportProgress(job.Id, p.percent, p.message);
            });

            // Check for stop requests periodically
            _ = Task.Run(async () =>
            {
                while (!jobCts.IsCancellationRequested)
                {
                    if (job.StopRequested)
                    {
                        jobCts.Cancel();
                        break;
                    }
                    await Task.Delay(250, CancellationToken.None);
                }
            }, CancellationToken.None);

            await _engine.ProcessJobAsync(job, progress, jobCts.Token);

            if (job.StopRequested)
            {
                job.Status = JobStatusEnum.Cancelled;
                _notifier.ReportStatusChange(job.Id, JobStatusEnum.Cancelled);
            }
            else
            {
                job.Status = JobStatusEnum.Done;
                job.Progress = 100;
                job.CompletedAt = DateTime.UtcNow;
                _notifier.ReportStatusChange(job.Id, JobStatusEnum.Done);
                _notifier.ReportJobCompleted(job.Id, job.OutputPath ?? "");
            }

            await SaveToHistoryAsync(job);
        }
        catch (OperationCanceledException)
        {
            job.Status = JobStatusEnum.Cancelled;
            _notifier.ReportStatusChange(job.Id, JobStatusEnum.Cancelled);
            await SaveToHistoryAsync(job);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing job {JobId}", job.Id);
            job.Status = JobStatusEnum.Error;
            job.Error = ex.Message;
            _notifier.ReportStatusChange(job.Id, JobStatusEnum.Error, ex.Message);
            _notifier.ReportJobError(job.Id, ex.Message);
            await SaveToHistoryAsync(job);
        }
        finally
        {
            _concurrency.Release();
            _notifier.NotifyJobListChanged();
        }
    }

    private async Task SaveToHistoryAsync(RecordingJob job)
    {
        try
        {
            var entry = new JobHistoryEntry
            {
                Id = job.Id,
                SourceName = job.SourceName,
                SourceType = job.SourceType,
                SourceUrl = job.Url,
                UsedTemplate = job.UseTemplate,
                TemplateId = job.TemplateId,
                Fps = job.Options.Fps,
                Duration = job.Options.Duration,
                Width = job.Options.Width,
                Height = job.Options.Height,
                PageZoom = job.Options.PageZoom,
                FinalStatus = job.Status,
                OutputPath = job.OutputPath,
                ErrorMessage = job.Error,
                CreatedAt = job.CreatedAt,
                CompletedAt = job.CompletedAt,
                ProcessingTime = job.CompletedAt.HasValue && job.StartedAt.HasValue
                    ? job.CompletedAt.Value - job.StartedAt.Value
                    : null
            };

            if (job.OutputPath != null && File.Exists(job.OutputPath))
            {
                entry.OutputFileSize = new FileInfo(job.OutputPath).Length;
            }

            await _jobRepository.SaveAsync(entry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save job {JobId} to history", job.Id);
        }
    }
}
