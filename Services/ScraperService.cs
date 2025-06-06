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

    public async Task<List<ScrapeResult>> RunScrapingAsync()
    {
        return await _repo.ProcessCompanyAsync();
    }

    public async Task<List<CompanyItem>> GetAllCompaniesAsync()
    {
        return await _repo.GetAllCompaniesAsync();
    }
}

}
