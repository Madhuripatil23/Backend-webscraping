using webscrapperapi.Models;
using webscrapperapi.Helpers; // WebScraper, FileDownloader
using webscrapperapi.Services; // GeminiService
using System.Data.SqlClient;
using System.Data;
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

            // Existing combinations
            var existingCompanyYmSet = allCompanies
                .Where(c => c.Period != null)
                .SelectMany(c => c.Period.Select(p => new { c.CompanyId, p.YM }))
                .ToHashSet();

            var companiesToProcess = allCompanies
                .GroupBy(c => c.CompanyId)
                .Select(g => g.First())
                .ToList();

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

                if (scrapeResults == null || scrapeResults.Count == 0)
                {
                    results.Add(new ScrapeResult
                    {
                        Symbol = company.Symbol,
                        Summary = "No results found for company URL"
                    });
                    continue;
                }

                foreach (var result in scrapeResults)
                {
                    string transcriptUrl = result.TranscriptUrl;
                    string pptUrl = result.PptUrl;
                    string dateTag = result.DateTag;
                    string ym = MonthTagConverter.ToYearMonth(dateTag ?? "unknown").Replace("-", "_");
                    string baseDir = Path.Combine("Downloads", company.Symbol, ym);

                    if (existingCompanyYmSet.Contains(new { company.CompanyId, YM = ym }))
                    {
                        Console.WriteLine($"[SKIP] {company.Symbol} for {ym} already exists.");
                        continue;
                    }

                    Directory.CreateDirectory(baseDir);

                    string? transcriptPath = null;
                    string? pptPath = null;
                    string summary = "No file downloaded, skipping Gemini.";
                    string outJson = Path.Combine(baseDir, $"{company.Symbol}_{ym}_summary.json");

                    try
                    {
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
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Download failed for {company.Symbol} ({ym}): {ex.Message}");
                    }

                    if ((transcriptPath != null && File.Exists(transcriptPath)) || (pptPath != null && File.Exists(pptPath)))
                    {
                        try
                        {
                            summary = await _geminiService.ExtractDataFromPdfAsync(transcriptPath, pptPath, company.Symbol);
                            await File.WriteAllTextAsync(outJson, summary);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Gemini processing failed for {company.Symbol} ({ym}): {ex.Message}");
                            summary = $"Gemini processing failed: {ex.Message}";
                        }
                    }

                    baseDir = Path.Combine("Downloads", company.Symbol);

                    // Balacesheet & Shareholding Pattern
                    string balanceSheet = await WebScraper.ScrapeBalanceSheetAsync(company.ScreenerUrl);
                    string shareholding = await WebScraper.ScrapeShareholdingPatternAsync(company.ScreenerUrl);


                    Console.WriteLine($"[DEBUG] Balance sheet for {company.Symbol}: {(string.IsNullOrWhiteSpace(balanceSheet) ? "EMPTY" : "OK")}");
                    Console.WriteLine($"[DEBUG] Shareholding for {company.Symbol}: {(string.IsNullOrWhiteSpace(shareholding) ? "EMPTY" : "OK")}");


                    if (!string.IsNullOrWhiteSpace(balanceSheet) && !string.IsNullOrWhiteSpace(shareholding))
                    {
                        string summaryPath = Path.Combine(baseDir, ym, $"{company.Symbol}_{ym}_summary.json");

                        string balancePath = Path.Combine(baseDir, $"{company.Symbol}_{ym}_balance_sheet.json");
                        await File.WriteAllTextAsync(balancePath, balanceSheet);

                        string sharePath = Path.Combine(baseDir, $"{company.Symbol}_{ym}_shareholding.json");
                        await File.WriteAllTextAsync(sharePath, shareholding);

                        // Load existing summary json
                        var summaryJson = JObject.Parse(await File.ReadAllTextAsync(summaryPath));

                        // Parse balance sheet JSON
                        var balanceSheetJson = JObject.Parse(balanceSheet);
                        

                        // Parse shareholding JSON
                        var shareHoldingJson = JObject.Parse(shareholding);


                        // Inject balance & shareholding sheet into summary JSON
                        summaryJson["balance_sheet"] = balanceSheetJson;
                        summaryJson["shareholding"] = shareHoldingJson;

                        // Save updated summary
                        await File.WriteAllTextAsync(summaryPath, summaryJson.ToString(Formatting.Indented));
                    }



                    if (!string.IsNullOrWhiteSpace(shareholding))
                    {
                        string sharePath = Path.Combine(baseDir, $"{company.Symbol}_{ym}_shareholding.json");
                        await File.WriteAllTextAsync(sharePath, shareholding);
                    }



                    try
                    {
                        await InsertReportRecordAsync(company.CompanyId, ym, pptUrl, pptPath, transcriptUrl, transcriptPath, outJson);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"DB Insert failed: {ex.Message}");
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
