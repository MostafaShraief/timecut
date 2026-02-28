using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Timecut.Core.Events;
using Timecut.Core.Interfaces;
using Timecut.Infrastructure.FFmpeg;
using Timecut.Infrastructure.FileManagement;
using Timecut.Infrastructure.Persistence;
using Timecut.Infrastructure.Playwright;
using Timecut.Infrastructure.Processing;
using Timecut.Infrastructure.Settings;

namespace Timecut.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTimecutInfrastructure(this IServiceCollection services)
    {
        // Events
        services.AddSingleton<ProgressNotifier>();

        // File management
        services.AddSingleton<IFileManager, FileManager>();

        // Settings
        services.AddSingleton<ISettingsService, SettingsService>();

        // Processing
        services.AddSingleton<IJobQueue, JobQueue>();
        services.AddSingleton<ConcurrencyControl>();
        services.AddSingleton<IRecordingEngine, RecordingEngine>();
        services.AddHostedService<RecordingWorker>();

        // Playwright
        services.AddSingleton<BrowserManager>();
        services.AddSingleton<PlaywrightService>();

        // FFmpeg
        services.AddSingleton<FFmpegService>();

        // Persistence
        services.AddDbContext<TimecutDbContext>(ServiceLifetime.Transient);
        services.AddTransient<IPresetRepository, PresetRepository>();
        services.AddTransient<IJobRepository, JobRepository>();
        services.AddTransient<ITemplateRepository, TemplateRepository>();

        return services;
    }

    /// <summary>
    /// Ensures the database is created and migrations are applied.
    /// Call this during app startup.
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TimecutDbContext>();
        await db.Database.EnsureCreatedAsync();
    }
}
