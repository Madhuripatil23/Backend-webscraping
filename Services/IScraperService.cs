using webscrapperapi.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace webscrapperapi.Services
{
    public interface IScraperService
    {
        Task<List<ScrapeResult>> RunScrapingAsync();
        Task<List<CompanyItem>> GetAllCompaniesAsync();

    }
}
