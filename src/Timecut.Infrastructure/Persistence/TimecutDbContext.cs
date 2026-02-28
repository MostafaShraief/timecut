using Microsoft.EntityFrameworkCore;
using Timecut.Core.Models;

namespace Timecut.Infrastructure.Persistence;

public class TimecutDbContext : DbContext
{
    public DbSet<RecordingPreset> Presets { get; set; } = null!;
    public DbSet<JobHistoryEntry> JobHistory { get; set; } = null!;
    public DbSet<HtmlTemplate> Templates { get; set; } = null!;

    private readonly string _dbPath;

    public TimecutDbContext()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Timecut");
        Directory.CreateDirectory(appDataDir);
        _dbPath = Path.Combine(appDataDir, "timecut.db");
    }

    public TimecutDbContext(string dbPath)
    {
        _dbPath = dbPath;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSqlite($"Data Source={_dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RecordingPreset>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.OwnsOne(e => e.Options, owned =>
            {
                owned.Property(o => o.Fps).HasColumnName("Fps");
                owned.Property(o => o.Duration).HasColumnName("Duration");
                owned.Property(o => o.Width).HasColumnName("Width");
                owned.Property(o => o.Height).HasColumnName("Height");
                owned.Property(o => o.PageZoom).HasColumnName("PageZoom");
                owned.Property(o => o.PixelFormat).HasColumnName("PixelFormat");
                owned.Property(o => o.ScreenshotFormat).HasColumnName("ScreenshotFormat");
                owned.Property(o => o.ScreenshotQuality).HasColumnName("ScreenshotQuality");
                owned.Property(o => o.StartOffset).HasColumnName("StartOffset");
                owned.Property(o => o.VideoCodec).HasColumnName("VideoCodec");
                owned.Property(o => o.EncodingPreset).HasColumnName("EncodingPreset");
                owned.Property(o => o.OutputFileName).HasColumnName("OutputFileName");
            });
        });

        modelBuilder.Entity<JobHistoryEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SourceType).HasConversion<string>();
            entity.Property(e => e.FinalStatus).HasConversion<string>();
        });

        modelBuilder.Entity<HtmlTemplate>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Content).HasColumnType("TEXT");
        });
    }
}
