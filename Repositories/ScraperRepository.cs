using webscrapperapi.Models;
using webscrapperapi.Helpers; // WebScraper, FileDownloader
using webscrapperapi.Services; // GeminiService
using System.Data.SqlClient;
using System.Data;
using System;

namespace webscrapperapi.Repositories
{
    public class ScraperRepository : IScraperRepository
    {
        private readonly GeminiService _geminiService;

        private readonly string _connectionString;

        public ScraperRepository(IConfiguration configuration, GeminiService geminiService)
        {
            _geminiService = geminiService ?? throw new ArgumentNullException(nameof(geminiService));
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        public async Task<List<ScrapeResult>> ProcessCompanyAsync(int userId)
        {
            var allCompanies = await GetAllCompaniesAsync(userId);
            string currentYM = $"{DateTime.Now:yyyy}_{(((DateTime.Now.Month - 1) / 3) * 3) + 1:00}";

            // Filter companies that do NOT have current YM reports
            var companiesToProcess = allCompanies
    .GroupBy(c => c.CompanyId)
    .Where(g => !g.Any(r => r.Period != null && r.Period.Any(ym => ym.YM == currentYM)))
    .Select(g => g.First()) // pick one entry per company
    .ToList();

            if (companiesToProcess == null)
            {
                return new List<ScrapeResult>();
            }

            var results = new List<ScrapeResult>();

            foreach (var company in companiesToProcess)
            {
                if (string.IsNullOrWhiteSpace(company.ScreenerUrl))
                {
                    results.Add(new ScrapeResult
                    {
                        Symbol = company.Symbol,
                        Summary = "No Screener URL provided."
                    });
                    continue;
                }

                var scrapeResults = await WebScraper.ScrapeLinks(company.ScreenerUrl);
                var firstResult = scrapeResults.FirstOrDefault();

                if (firstResult == null)
                {
                    results.Add(new ScrapeResult
                    {
                        Symbol = company.Symbol,
                        Summary = "No results found for company URL"
                    });
                    continue;
                }

                string transcriptUrl = firstResult.TranscriptUrl;
                string pptUrl = firstResult.PptUrl;
                string dateTag = firstResult.DateTag;
                string ym = MonthTagConverter.ToYearMonth(dateTag ?? "unknown").Replace("-", "_");
                string baseDir = Path.Combine("Downloads", company.Symbol, ym);

                Directory.CreateDirectory(baseDir);

                string? transcriptPath = null;
                string? pptPath = null;

                if (!string.IsNullOrEmpty(transcriptUrl))
                {
                    transcriptPath = Path.Combine(baseDir, $"{company.Symbol}_{ym}_transcript.pdf");
                    await FileDownloader.DownloadPdfAsync(transcriptUrl, transcriptPath);
                }

                if (!string.IsNullOrEmpty(pptUrl))
                {
                    pptPath = Path.Combine(baseDir, $"{company.Symbol}_{ym}_ppt.pdf");
                    await FileDownloader.DownloadPdfAsync(pptUrl, pptPath);
                }

                string summary = "No file downloaded, skipping Gemini.";
                string outJson = Path.Combine(baseDir, $"{company.Symbol}_{ym}_summary.json");

                if ((transcriptPath != null && File.Exists(transcriptPath)) || (pptPath != null && File.Exists(pptPath)))
                {
                    summary = await _geminiService.ExtractDataFromPdfAsync(transcriptPath, pptPath, company.Symbol);
                    await File.WriteAllTextAsync(outJson, summary);

                    await InsertReportRecordAsync(company.CompanyId, ym, pptUrl, pptPath, transcriptUrl, transcriptPath, outJson);
                }

                results.Add(new ScrapeResult
                {
                    Symbol = company.Symbol,
                    TranscriptUrl = transcriptUrl,
                    PptUrl = pptUrl,
                    DateTag = dateTag,
                    Summary = summary
                });
            }
            return results;
        }


        public async Task<List<CompanyItem>> GetAllCompaniesAsync(int userId)
        {
            var companiesDict = new Dictionary<int, CompanyItem>();

            using (SqlConnection conn = new SqlConnection(_connectionString))
            using (SqlCommand cmd = new SqlCommand("usp_GetCompany", conn))
            {
                cmd.CommandType = CommandType.StoredProcedure;

                cmd.Parameters.AddWithValue("@UserId", userId);
                
                await conn.OpenAsync();

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        int companyId = Convert.ToInt32(reader["pkc_company_id"]);
                        string companyName = reader["c_company_name"]?.ToString() ?? "";
                        string symbol = reader["c_symbol"]?.ToString() ?? "";
                        string screenerUrl = reader["c_screener_url"]?.ToString() ?? "";
                        string reportId = reader["pkc_report_id"]?.ToString() ?? "";
                        string ymValue = reader["c_ym"]?.ToString() ?? "";
                        string pptUrl = reader["c_ppt_url"]?.ToString() ?? "";
                        string transcriptUrl = reader["c_transcript_url"]?.ToString() ?? "";
                        string summaryUrl = reader["c_summary_location"]?.ToString() ?? "";

                        if (!companiesDict.TryGetValue(companyId, out var company))
                        {
                            company = new CompanyItem
                            {
                                CompanyId = companyId,
                                CompanyName = companyName,
                                Symbol = symbol,
                                ScreenerUrl = screenerUrl,
                                Period = new List<Period>()
                            };
                            companiesDict[companyId] = company;
                        }

                        if (!string.IsNullOrWhiteSpace(ymValue) ||
                            !string.IsNullOrWhiteSpace(pptUrl) ||
                            !string.IsNullOrWhiteSpace(transcriptUrl) ||
                            !string.IsNullOrWhiteSpace(summaryUrl) ||
                            !string.IsNullOrWhiteSpace(reportId))
                        {
                            company.Period.Add(new Period
                            {
                                ReportId = reportId,
                                YM = ymValue,
                                PPTUrl = pptUrl,
                                TranscriptUrl = transcriptUrl,
                                SummaryUrl = summaryUrl
                            });
                        }
                    }
                }
            }

            return companiesDict.Values.ToList();
        }


        public async Task InsertReportRecordAsync(int companyId, string ym, string pptFileUrl, string pptFilePath, string transcriptFileUrl, string transcriptFilePath, string summaryFilePath)
        {
            using SqlConnection conn = new SqlConnection(_connectionString);
            using SqlCommand cmd = new SqlCommand("usp_InsertReport", conn)
            {
                CommandType = CommandType.StoredProcedure
            };

            cmd.Parameters.AddWithValue("@fkc_company_id", companyId);
            cmd.Parameters.AddWithValue("@c_ym", ym);
            cmd.Parameters.AddWithValue("@c_ppt_file_src_url", pptFileUrl ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@c_ppt_file_location", pptFilePath ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@c_transcript_file_src_url", transcriptFileUrl ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@c_transcript_file_location", transcriptFilePath ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@c_summary_location", summaryFilePath ?? (object)DBNull.Value);
            // Add other parameters as needed, e.g. created_by etc.

            await conn.OpenAsync();
            await cmd.ExecuteNonQueryAsync();
        }


    }
}
