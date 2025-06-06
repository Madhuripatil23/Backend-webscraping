using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace webscrapperapi.Services
{
    public class GeminiService
    {
        private const int MAX_INLINE_BYTES = 20 * 1024 * 1024; // 20 MB
        private const string DEFAULT_MODEL = "gemini-2.5-pro-preview-05-06";
        private const string SCHEMA_PATH = "Resources/FinancialAnalysisSchema.json";
        private const string PROMPT_TEMPLATE_PATH = "Resources/FinancialAnalysisPrompt.txt";

        public async Task<string> ExtractDataFromPdfAsync(string transcriptPdfPath, string pptPdfPath, string company)
        {
            var outJsonPath = Path.ChangeExtension(transcriptPdfPath ?? pptPdfPath, ".json");
            if (File.Exists(outJsonPath))
            {
                Console.WriteLine($"Summary exists – skip {Path.GetFileName(outJsonPath)}");
                return await File.ReadAllTextAsync(outJsonPath);
            }

            var parts = new List<Dictionary<string, object>>();
            foreach (var pdfPath in new[] { transcriptPdfPath, pptPdfPath })
            {
                if (!string.IsNullOrEmpty(pdfPath) && File.Exists(pdfPath))
                {
                    var data = await File.ReadAllBytesAsync(pdfPath);
                    if (data.Length <= MAX_INLINE_BYTES)
                    {
                        parts.Add(new Dictionary<string, object>
                        {
                            ["inline_data"] = new Dictionary<string, object>
                            {
                                ["mime_type"] = "application/pdf",
                                ["data"] = Convert.ToBase64String(data)
                            }
                        });
                    }
                    else
                    {
                        Console.WriteLine($"{Path.GetFileName(pdfPath)} > 20 MB – falling back to extracted text");
                        parts.Add(new Dictionary<string, object>
                        {
                            ["text"] = ExtractTextFromPdf(pdfPath)
                        });
                    }
                }
            }

            if (parts.Count == 0)
            {
                Console.WriteLine($"No PDFs found for {company} – skipping summary");
                return "No PDFs found";
            }

            string prompt = await BuildPromptAsync(company);

            try
            {
                string responseText = await CallGeminiApiAsync(prompt, parts);
                string jsonText = CleanResponseText(responseText);

                string outJsontext = Path.Combine("Downloads", "jsontext.txt");
                await File.WriteAllTextAsync(outJsontext,responseText);


                var jsonDoc = JsonDocument.Parse(jsonText);
                var jsonFormatted = JsonSerializer.Serialize(jsonDoc, new JsonSerializerOptions { WriteIndented = true });

                await File.WriteAllTextAsync(outJsonPath, jsonFormatted, Encoding.UTF8);
                Console.WriteLine($"Summary saved → {outJsonPath}");

                return jsonFormatted;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Gemini failure for {company}: {ex.Message}");
                return $"Error: {ex.Message}";
            }
        }

        private string ExtractTextFromPdf(string pdfPath)
        {
            var sb = new StringBuilder();
            using var document = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
            foreach (var page in document.GetPages())
            {
                sb.AppendLine(page.Text);
            }
            return sb.ToString();
        }

        private async Task<string> BuildPromptAsync(string company)
        {
            string schema = await File.ReadAllTextAsync(SCHEMA_PATH);
            string promptTemplate = await File.ReadAllTextAsync(PROMPT_TEMPLATE_PATH);

            // string outJson = Path.Combine("Downloads", "prompt.json");
            // await File.WriteAllTextAsync(outJson, promptTemplate.Replace("{company}", company).Replace("{SCHEMA_PATH}", schema));

            return promptTemplate.Replace("{company}", company).Replace("{SCHEMA_PATH}", schema);
        }

        private async Task<string> CallGeminiApiAsync(string prompt, List<Dictionary<string, object>> parts)
        {
            // Create the prompt part as text
            var promptPart = new Dictionary<string, object> { { "text", prompt } };

            // Combine prompt + pdf parts
            var allParts = new List<Dictionary<string, object>> { promptPart };
            if (parts != null && parts.Count > 0)
            {
                allParts.AddRange(parts);
            }

            var requestPayload = new
            {
                contents = new[]
                {
            new
            {
                role = "user",
                parts = allParts.ToArray()
            }
        },
                generationConfig = new
                {
                    temperature = 0.5,
                    topK = 1,
                    topP = 1,
                    maxOutputTokens = 8192
                }
            };

            string apiKey = "AIzaSyCoqLtsbTreGe2JqGtGB2ydpT63dAgJpNI";
            string endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-pro:generateContent?key={apiKey}";

            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(20) // example: 10 minutes timeout
            };
            var content = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync(endpoint, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Gemini API error: {response.StatusCode} - {responseBody}");

            var json = JsonDocument.Parse(responseBody);

            string modelResponse = json.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            return modelResponse;
        }


        private string CleanResponseText(string response)
        {
            return Regex.Replace(response, @"^```json|```$", "", RegexOptions.Multiline).Trim();
        }

    }
}
