
using Microsoft.AspNetCore.Mvc;
using webscrapperapi.Models;
using System.Text.Json;
using webscrapperapi.Services;
using webscrapperapi.Responses;
using System.Data.SqlClient;

namespace webscrapperapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ScrapperController : ControllerBase
    {
        private readonly IScraperService _scraperService;

        public ScrapperController(IScraperService scraperService)
        {
            _scraperService = scraperService;
        }


        [HttpPost("run")]
        public async Task<IActionResult> RunScraper([FromQuery] int userId)
        {
            try
            {
                var result = await _scraperService.RunScrapingAsync(userId);

                if (result == null)
                {
                    // Internal issue if null instead of empty list
                    return StatusCode(500, new ApiResponse<string>
                    {
                        Success = false,
                        Message = "Scraper returned null unexpectedly.",
                        ErrorCode = -2,
                        Data = null
                    });
                }

                if (!result.Any())
                {
                    // Still a successful operation, just no data to process
                    return Ok(new ApiResponse<List<ScrapeResult>>
                    {
                        Success = true,
                        Message = "No companies to process.",
                        ErrorCode = 0,
                        Data = new List<ScrapeResult>()
                    });
                }

                // Success with data
                return Ok(new ApiResponse<List<ScrapeResult>>
                {
                    Success = true,
                    Message = "Scraping completed successfully.",
                    ErrorCode = 0,
                    Data = result
                });
            }
            catch (ArgumentException ex)
            {
                // Example: if you want to treat bad arguments separately
                return BadRequest(new ApiResponse<string>
                {
                    Success = false,
                    Message = $"Bad input: {ex.Message}",
                    ErrorCode = -10,
                    Data = null
                });
            }
            catch (Exception ex)
            {
                // Unhandled server error
                return StatusCode(500, new ApiResponse<string>
                {
                    Success = false,
                    Message = $"An internal error occurred: {ex.Message}",
                    ErrorCode = -1,
                    Data = null
                });
            }
        }


        [HttpGet("company/tree")]
        public async Task<IActionResult> GetCompanies([FromQuery] int userId)
        {
            try
            {
                var companies = await _scraperService.GetAllCompaniesAsync(userId);
                if (companies == null || companies.Count == 0)
                {
                    return NotFound(ApiResponse<List<CompanyItem>>.ErrorResponse("No companies found", 404));
                }

                return Ok(ApiResponse<List<CompanyItem>>.SuccessResponse(companies, "Companies fetched successfully"));
                // return Ok(companies);
            }
            catch (UnauthorizedAccessException)
            {
                return StatusCode(403, ApiResponse<List<CompanyItem>>.ErrorResponse("Access denied", 403));
            }
            catch (SqlException)
            {
                return StatusCode(500, ApiResponse<List<CompanyItem>>.ErrorResponse("Database error occurred", 500));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<List<CompanyItem>>.ErrorResponse($"Unexpected error: {ex.Message}", 500));
            }
        }

        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary([FromQuery] int userId, [FromQuery] string reportId)
        {
            try
            {

                // Fetch all companies asynchronously
                var companies = await _scraperService.GetAllCompaniesAsync(userId);
                if (companies == null || !companies.Any())
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "No companies found.",
                        error_code = 404,
                        data = (object)null
                    });
                }

                CompanyItem matchedCompany = null;
                Period matchedPeriod = null;

                foreach (var company in companies)
                {
                    if (company.Period != null)
                    {
                        matchedPeriod = company.Period.FirstOrDefault(p => p.ReportId == reportId);
                        if (matchedPeriod != null)
                        {
                            matchedCompany = company;
                            break;
                        }
                    }
                }

                if (matchedCompany == null || matchedPeriod == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Report Id not found.",
                        error_code = 404,
                        data = (object)null
                    });
                }

                string summaryPath = matchedPeriod.SummaryUrl;

                Console.WriteLine(summaryPath);
                if (!System.IO.File.Exists(summaryPath))
                    return NotFound(new { message = "Summary file not found." });

                string jsonContent = System.IO.File.ReadAllText(summaryPath);
                var jsonData = JsonSerializer.Deserialize<object>(jsonContent); // or strongly type if known

                return Ok(jsonData);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error reading summary file.", error = ex.Message });
            }
        }


        [HttpGet("download")]
        public async Task<IActionResult> DownloadFile([FromQuery] int userId, [FromQuery] string reportId, [FromQuery] string type)
        {
            if (string.IsNullOrWhiteSpace(reportId) || string.IsNullOrWhiteSpace(type))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Report Id and type are required.",
                    error_code = 400,
                    data = (object)null
                });
            }

            // Fetch all companies asynchronously
            var companies = await _scraperService.GetAllCompaniesAsync(userId);
            if (companies == null || !companies.Any())
            {
                return NotFound(new
                {
                    success = false,
                    message = "No companies found.",
                    error_code = 404,
                    data = (object)null
                });
            }

            CompanyItem matchedCompany = null;
            Period matchedPeriod = null;

            foreach (var company in companies)
            {
                if (company.Period != null)
                {
                    matchedPeriod = company.Period.FirstOrDefault(p => p.ReportId == reportId);
                    if (matchedPeriod != null)
                    {
                        matchedCompany = company;
                        break;
                    }
                }
            }

            if (matchedCompany == null || matchedPeriod == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "Report Id not found.",
                    error_code = 404,
                    data = (object)null
                });
            }

            // Select the file path based on 'type'
            string filePath = null;
            if (type.Equals("ppt", StringComparison.OrdinalIgnoreCase))
            {
                filePath = matchedPeriod.PPTUrl;
            }
            else if (type.Equals("transcript", StringComparison.OrdinalIgnoreCase))
            {
                filePath = matchedPeriod.TranscriptUrl;
            }
            else
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Invalid type parameter. Allowed values: 'ppt' or 'transcript'.",
                    error_code = 400,
                    data = (object)null
                });
            }

            if (string.IsNullOrWhiteSpace(filePath))
            {
                return NotFound(new
                {
                    success = false,
                    message = $"File path for type '{type}' not found for the given report.",
                    error_code = 404,
                    data = (object)null
                });
            }

            // Build the absolute file path - assuming filePath is relative to your app directory or a configured root
            var rootPath = Path.Combine(Directory.GetCurrentDirectory(), ""); // or your base folder
            var fullPath = Path.Combine(rootPath, filePath.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
            Console.WriteLine(fullPath);
            if (!System.IO.File.Exists(fullPath))
            {
                return NotFound(new
                {
                    success = false,
                    message = "File not found on server.",
                    error_code = 404,
                    data = (object)null
                });
            }

            var contentType = GetContentType(fullPath);
            var fileName = Path.GetFileName(fullPath);
            var fileBytes = System.IO.File.ReadAllBytes(fullPath);

            Response.Headers.Add("Access-Control-Expose-Headers", "Content-Disposition");

            return File(fileBytes, contentType, fileName);
        }

        // Optional: Map file extensions to MIME types
        private string GetContentType(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();

            return ext switch
            {
                ".pdf" => "application/pdf",
                ".doc" or ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xls" or ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".txt" => "text/plain",
                _ => "application/octet-stream",
            };
        }
    }
}
