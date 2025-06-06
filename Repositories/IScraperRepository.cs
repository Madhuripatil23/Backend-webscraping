using webscrapperapi.Models;
using System.Threading.Tasks;

namespace webscrapperapi.Repositories
{
    public interface IScraperRepository
    {
        Task<List<ScrapeResult>> ProcessCompanyAsync();
        Task<List<CompanyItem>> GetAllCompaniesAsync();
    }
    
}
