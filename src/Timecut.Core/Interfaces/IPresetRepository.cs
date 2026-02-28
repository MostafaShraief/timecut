using Timecut.Core.Models;

namespace Timecut.Core.Interfaces;

public interface IPresetRepository
{
    Task<List<RecordingPreset>> GetAllAsync();
    Task<RecordingPreset?> GetByIdAsync(string id);
    Task SaveAsync(RecordingPreset preset);
    Task DeleteAsync(string id);
}
