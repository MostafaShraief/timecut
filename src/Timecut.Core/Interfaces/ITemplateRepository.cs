using Timecut.Core.Models;

namespace Timecut.Core.Interfaces;

public interface ITemplateRepository
{
    Task<List<HtmlTemplate>> GetAllAsync();
    Task<HtmlTemplate?> GetByIdAsync(string id);
    Task SaveAsync(HtmlTemplate template);
    Task DeleteAsync(string id);
}
