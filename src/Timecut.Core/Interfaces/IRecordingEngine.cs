using Timecut.Core.Models;

namespace Timecut.Core.Interfaces;

public interface IRecordingEngine
{
    Task ProcessJobAsync(RecordingJob job, IProgress<(double percent, string message)> progress, CancellationToken cancellationToken);
}
