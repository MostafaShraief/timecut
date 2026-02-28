using Timecut.Core.Models;

namespace Timecut.Core.Interfaces;

public interface IJobRepository
{
    Task<List<JobHistoryEntry>> GetAllAsync(int? limit = null);
    Task<JobHistoryEntry?> GetByIdAsync(string id);
    Task SaveAsync(JobHistoryEntry entry);
    Task DeleteAsync(string id);
    Task ClearAllAsync();
}
