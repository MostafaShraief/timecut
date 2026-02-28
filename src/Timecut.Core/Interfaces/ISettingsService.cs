using Timecut.Core.Models;

namespace Timecut.Core.Interfaces;

public interface ISettingsService
{
    AppSettings GetSettings();
    Task SaveSettingsAsync(AppSettings settings);
    T Get<T>(string key, T defaultValue);
    Task SetAsync<T>(string key, T value);
}
