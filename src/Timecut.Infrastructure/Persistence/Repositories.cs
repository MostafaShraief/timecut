using Microsoft.EntityFrameworkCore;
using Timecut.Core.Interfaces;
using Timecut.Core.Models;

namespace Timecut.Infrastructure.Persistence;

public class PresetRepository : IPresetRepository
{
    private readonly TimecutDbContext _db;

    public PresetRepository(TimecutDbContext db)
    {
        _db = db;
    }

    public async Task<List<RecordingPreset>> GetAllAsync()
    {
        return await _db.Presets.OrderBy(p => p.Name).ToListAsync();
    }

    public async Task<RecordingPreset?> GetByIdAsync(string id)
    {
        return await _db.Presets.FindAsync(id);
    }

    public async Task SaveAsync(RecordingPreset preset)
    {
        var existing = await _db.Presets.FindAsync(preset.Id);
        if (existing != null)
        {
            _db.Entry(existing).CurrentValues.SetValues(preset);
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.Presets.Add(preset);
        }
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(string id)
    {
        var preset = await _db.Presets.FindAsync(id);
        if (preset != null)
        {
            _db.Presets.Remove(preset);
            await _db.SaveChangesAsync();
        }
    }
}

public class JobRepository : IJobRepository
{
    private readonly TimecutDbContext _db;

    public JobRepository(TimecutDbContext db)
    {
        _db = db;
    }

    public async Task<List<JobHistoryEntry>> GetAllAsync(int? limit = null)
    {
        var query = _db.JobHistory.OrderByDescending(j => j.CreatedAt).AsQueryable();
        if (limit.HasValue)
            query = query.Take(limit.Value);
        return await query.ToListAsync();
    }

    public async Task<JobHistoryEntry?> GetByIdAsync(string id)
    {
        return await _db.JobHistory.FindAsync(id);
    }

    public async Task SaveAsync(JobHistoryEntry entry)
    {
        var existing = await _db.JobHistory.FindAsync(entry.Id);
        if (existing != null)
        {
            _db.Entry(existing).CurrentValues.SetValues(entry);
        }
        else
        {
            _db.JobHistory.Add(entry);
        }
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(string id)
    {
        var entry = await _db.JobHistory.FindAsync(id);
        if (entry != null)
        {
            _db.JobHistory.Remove(entry);
            await _db.SaveChangesAsync();
        }
    }

    public async Task ClearAllAsync()
    {
        _db.JobHistory.RemoveRange(_db.JobHistory);
        await _db.SaveChangesAsync();
    }
}

public class TemplateRepository : ITemplateRepository
{
    private readonly TimecutDbContext _db;

    public TemplateRepository(TimecutDbContext db)
    {
        _db = db;
    }

    public async Task<List<HtmlTemplate>> GetAllAsync()
    {
        return await _db.Templates.OrderBy(t => t.Name).ToListAsync();
    }

    public async Task<HtmlTemplate?> GetByIdAsync(string id)
    {
        return await _db.Templates.FindAsync(id);
    }

    public async Task SaveAsync(HtmlTemplate template)
    {
        var existing = await _db.Templates.FindAsync(template.Id);
        if (existing != null)
        {
            _db.Entry(existing).CurrentValues.SetValues(template);
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.Templates.Add(template);
        }
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(string id)
    {
        var template = await _db.Templates.FindAsync(id);
        if (template != null)
        {
            _db.Templates.Remove(template);
            await _db.SaveChangesAsync();
        }
    }
}
