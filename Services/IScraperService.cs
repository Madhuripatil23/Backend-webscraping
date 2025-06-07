using webscrapperapi.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace webscrapperapi.Services
{
    public interface IScraperService
    {
        Task<List<ScrapeResult>> RunScrapingAsync(int userId);
        Task<List<CompanyItem>> GetAllCompaniesAsync(int userId);

    }
}
