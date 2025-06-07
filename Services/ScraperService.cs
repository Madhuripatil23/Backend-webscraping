using System.Data;
using System.Data.SqlClient;
using webscrapperapi.Models;
using webscrapperapi.Repositories;

namespace webscrapperapi.Services
{
    public class ScraperService : IScraperService
{
    private readonly IScraperRepository _repo;

    public ScraperService(IScraperRepository repo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    }

    public async Task<List<ScrapeResult>> RunScrapingAsync(int userId)
    {
        return await _repo.ProcessCompanyAsync(userId);
    }

    public async Task<List<CompanyItem>> GetAllCompaniesAsync(int userId)
    {
        return await _repo.GetAllCompaniesAsync(userId);
    }
}

}
