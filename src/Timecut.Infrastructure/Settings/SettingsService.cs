using System.Text.Json;
using Timecut.Core.Interfaces;
using Timecut.Core.Models;

namespace Timecut.Infrastructure.Settings;

/// <summary>
/// Persists AppSettings and arbitrary key-value pairs to a JSON file
/// in the user's local application data folder.
/// </summary>
public class SettingsService : ISettingsService
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Timecut");

    private static readonly string SettingsFile =
        Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private SettingsData? _cache;

    public AppSettings GetSettings()
    {
        var data = LoadData();
        return data.AppSettings ?? new AppSettings();
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        await _lock.WaitAsync();
        try
        {
            var data = LoadData();
            data.AppSettings = settings;
            _cache = data;
            await SaveDataAsync(data);
        }
        finally
        {
            _lock.Release();
        }
    }

    public T Get<T>(string key, T defaultValue)
    {
        var data = LoadData();
        if (data.Values.TryGetValue(key, out var element))
        {
            try
            {
                return element.Deserialize<T>(JsonOptions) ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }
        return defaultValue;
    }

    public async Task SetAsync<T>(string key, T value)
    {
        await _lock.WaitAsync();
        try
        {
            var data = LoadData();
            data.Values[key] = JsonSerializer.SerializeToElement(value, JsonOptions);
            _cache = data;
            await SaveDataAsync(data);
        }
        finally
        {
            _lock.Release();
        }
    }

    private SettingsData LoadData()
    {
        if (_cache != null) return _cache;

        if (!File.Exists(SettingsFile))
        {
            _cache = new SettingsData();
            return _cache;
        }

        try
        {
            var json = File.ReadAllText(SettingsFile);
            _cache = JsonSerializer.Deserialize<SettingsData>(json, JsonOptions) ?? new SettingsData();
        }
        catch
        {
            _cache = new SettingsData();
        }

        return _cache;
    }

    private async Task SaveDataAsync(SettingsData data)
    {
        Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(data, JsonOptions);
        await File.WriteAllTextAsync(SettingsFile, json);
    }

    private class SettingsData
    {
        public AppSettings? AppSettings { get; set; } = new();
        public Dictionary<string, JsonElement> Values { get; set; } = new();
    }
}
