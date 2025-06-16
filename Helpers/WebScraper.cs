using HtmlAgilityPack;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using SeleniumExtras.WaitHelpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using webscrapperapi.Models;
using Newtonsoft.Json;


namespace webscrapperapi.Helpers
{
    public class WebScraper
    {
        private const string XPATH_UL = "/html/body/main/section[10]/div[2]/div[4]/div[2]/ul";
        private const string SHOW_MORE_XPATH = "/html/body/main/section[10]/div[2]/div[4]/div[2]/button";
        private const int TIMEOUT_SEC = 30;
        private const int MAX_SCRAPE_RETRY = 3;

        public static async Task<List<ScrapeResult>> ScrapeLinks(string pageUrl)
        {
            if (string.IsNullOrWhiteSpace(pageUrl))
                throw new ArgumentException("Scrape URL cannot be null or empty.", nameof(pageUrl));

            for (int attempt = 0; attempt < MAX_SCRAPE_RETRY; attempt++)
            {
                ChromeDriver driver = null;
                try
                {
                    var options = new ChromeOptions();
                    options.AddArgument("--headless");
                    options.AddArgument("--disable-gpu");
                    options.AddArgument("--no-sandbox");
                    options.AddArgument("--disable-dev-shm-usage");
                    options.AddArgument("--window-size=1920,1080");

                    driver = new ChromeDriver(options);
                    driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(TIMEOUT_SEC);
                    driver.Manage().Timeouts().AsynchronousJavaScript = TimeSpan.FromSeconds(TIMEOUT_SEC);
                    driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(10);

                    driver.Navigate().GoToUrl(pageUrl);

                    WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(TIMEOUT_SEC));
                    wait.Until(ExpectedConditions.ElementExists(By.XPath(XPATH_UL)));

                    var ulElem = ExpandUl(driver, XPATH_UL, SHOW_MORE_XPATH);
                    if (ulElem == null || ulElem.Count == 0)
                        throw new Exception("No <li> elements found in the target <ul>");

                    var results = new List<ScrapeResult>();

                    foreach (var li in ulElem)
                    {
                        var dateDivs = li.FindElements(By.TagName("div"));
                        if (dateDivs.Count == 0) continue;

                        string dateTag = dateDivs[0].Text.Trim();
                        string transcript = null, ppt = null;

                        foreach (var a in li.FindElements(By.TagName("a")))
                        {
                            string txt = a.Text.Trim().ToLower();
                            string href = a.GetAttribute("href");
                            if (txt.Contains("transcript")) transcript = href;
                            else if (txt == "ppt") ppt = href;
                        }

                        if (transcript != null || ppt != null)
                        {
                            results.Add(new ScrapeResult
                            {
                                TranscriptUrl = transcript,
                                PptUrl = ppt,
                                DateTag = dateTag
                            });

                            Console.WriteLine($"[Extracted] {dateTag} | Transcript: {transcript != null} | PPT: {ppt != null}");
                        }
                    }

                    return results;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Attempt {attempt + 1}] Scrape failed: {ex.Message}");
                    await Task.Delay(3000);
                    continue;
                }
                finally
                {
                    driver?.Quit();
                }
            }

            Console.WriteLine("All scrape retries failed.");
            return new List<ScrapeResult>();
        }

        private static List<IWebElement> ExpandUl(IWebDriver driver, string ulXPath, string buttonXPath, int maxWaitSeconds = 20)
        {
            try
            {
                var ul = driver.FindElement(By.XPath(ulXPath));
                int lastCount = 0;
                DateTime start = DateTime.Now;

                while (true)
                {
                    var lis = ul.FindElements(By.TagName("li")).ToList();
                    int currentCount = lis.Count;

                    if (currentCount == lastCount || (DateTime.Now - start).TotalSeconds > maxWaitSeconds)
                        return lis;

                    lastCount = currentCount;

                    try
                    {
                        var showMore = driver.FindElement(By.XPath(buttonXPath));
                        if (showMore.Displayed && showMore.Enabled)
                        {
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView(true);", showMore);
                            showMore.Click();
                            Thread.Sleep(1000); // Give time for new data to load
                        }
                        else
                        {
                            break;
                        }
                    }
                    catch (NoSuchElementException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("ExpandUl click error: " + ex.Message);
                    }
                }

                return ul.FindElements(By.TagName("li")).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine("ExpandUl failed: " + ex.Message);
                return new List<IWebElement>();
            }
        }

        // public static async Task<string?> ScrapeBalanceSheetAsync(string pageUrl)
        // {
        //     var options = new ChromeOptions();
        //     options.AddArgument("--headless");
        //     options.AddArgument("--no-sandbox");
        //     options.AddArgument("--disable-dev-shm-usage");

        //     using var driver = new ChromeDriver(options);
        //     driver.Navigate().GoToUrl(pageUrl);

        //     try
        //     {
        //         // Wait for the table inside the balance sheet section
        //         WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
        //         wait.Until(d => d.FindElement(By.CssSelector("#balance-sheet table")));

        //         // Extract the table HTML
        //         var balanceSheetTable = driver.FindElement(By.CssSelector("#balance-sheet table"));
        //         string balanceHtml = balanceSheetTable.GetAttribute("outerHTML");

        //         // Parse with HtmlAgilityPack
        //         var doc = new HtmlDocument();
        //         doc.LoadHtml(balanceHtml);

        //         var table = doc.DocumentNode.SelectSingleNode("//table");
        //         var headers = table.SelectNodes(".//thead/tr/th")
        //                           .Select(th => th.InnerText.Trim())
        //                           .ToList();

        //         var rows = new List<Dictionary<string, string>>();

        //         foreach (var row in table.SelectNodes(".//tbody/tr"))
        //         {
        //             var cells = row.SelectNodes("td");
        //             if (cells == null) continue;

        //             var rowDict = new Dictionary<string, string>();

        //             for (int i = 0; i < cells.Count; i++)
        //             {
        //                 string header = i < headers.Count ? headers[i] : $"Column{i}";
        //                 string value = cells[i].InnerText.Trim();
        //                 rowDict[header] = value;
        //             }

        //             rows.Add(rowDict);
        //         }

        //         return JsonConvert.SerializeObject(rows, Formatting.Indented);
        //     }
        //     catch (NoSuchElementException)
        //     {
        //         Console.WriteLine("Balance sheet table not found.");
        //         return null;
        //     }
        //     catch (WebDriverTimeoutException)
        //     {
        //         Console.WriteLine("Timed out waiting for balance sheet table.");
        //         return null;
        //     }
        //     catch (Exception ex)
        //     {
        //         Console.WriteLine($"Unexpected error: {ex.Message}");
        //         return null;
        //     }
        // }

        public static async Task<string?> ScrapeBalanceSheetAsync(string pageUrl)
        {
            var options = new ChromeOptions();
            options.AddArgument("--headless");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");

            using var driver = new ChromeDriver(options);
            driver.Navigate().GoToUrl(pageUrl);

            try
            {
                // Wait for the balance sheet table
                WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
                wait.Until(d => d.FindElement(By.CssSelector("#balance-sheet table")));

                // Get HTML of the balance sheet table
                var balanceSheetTable = driver.FindElement(By.CssSelector("#balance-sheet table"));
                string balanceHtml = balanceSheetTable.GetAttribute("outerHTML");

                // Load HTML into HtmlAgilityPack
                var doc = new HtmlDocument();
                doc.LoadHtml(balanceHtml);

                var table = doc.DocumentNode.SelectSingleNode("//table");
                var headers = table.SelectNodes(".//thead/tr/th")
                    .Select(th => th.InnerText.Replace("&nbsp;", " ").Trim())
                    .ToList();

                var rows = new List<Dictionary<string, string>>();

                foreach (var row in table.SelectNodes(".//tbody/tr"))
                {
                    var cells = row.SelectNodes("td");
                    if (cells == null) continue;

                    var rowDict = new Dictionary<string, string>();
                    for (int i = 0; i < cells.Count; i++)
                    {
                        string header = i < headers.Count ? headers[i] : $"Column{i}";
                        string value = cells[i].InnerText.Replace("&nbsp;", " ").Trim();
                        rowDict[header] = value;
                    }

                    rows.Add(rowDict);
                }

                // Transform to Dictionary<string, Dictionary<string, string>>
                var result = new Dictionary<string, Dictionary<string, string>>();

                foreach (var row in rows)
                {
                    if (row.Count == 0) continue;

                    var firstKey = row.Keys.First();
                    var label = row[firstKey].Replace("&nbsp;", " ").Trim();

                    var yearData = new Dictionary<string, string>();
                    foreach (var kvp in row)
                    {
                        if (kvp.Key == firstKey) continue;
                        yearData[kvp.Key.Trim()] = kvp.Value.Trim();
                    }

                    result[label] = yearData;
                }

                return JsonConvert.SerializeObject(result, Formatting.Indented);
            }
            catch (NoSuchElementException)
            {
                Console.WriteLine("Balance sheet table not found.");
                return null;
            }
            catch (WebDriverTimeoutException)
            {
                Console.WriteLine("Timed out waiting for balance sheet table.");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.Message}");
                return null;
            }
        }


        public static async Task<string?> ScrapeShareholdingPatternAsync(string pageUrl)
        {
            var options = new ChromeOptions();
            options.AddArgument("--headless");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");

            using var driver = new ChromeDriver(options);
            driver.Navigate().GoToUrl(pageUrl);

            try
            {
                WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));

                // Wait for both tables to be present
                wait.Until(d => d.FindElement(By.Id("quarterly-shp")));
                wait.Until(d => d.FindElement(By.Id("yearly-shp")));

                var quarterlyTable = driver.FindElement(By.Id("quarterly-shp"));

                var quarterlyData = ParseTable(quarterlyTable);


                var yearlyTable = driver.FindElement(By.Id("yearly-shp"));

                // Remove 'hidden' class so the table becomes visible
                IJavaScriptExecutor js = (IJavaScriptExecutor)driver;
                js.ExecuteScript("arguments[0].classList.remove('hidden');", yearlyTable);

                // Wait until visible
                wait.Until(d => yearlyTable.Displayed);

                var yearlyData = ParseTable(yearlyTable);

                var combinedData = new Dictionary<string, object>
        {
            { "quarterly", quarterlyData },
            { "yearly", yearlyData }
        };

                return JsonConvert.SerializeObject(combinedData, Formatting.Indented);
            }
            catch (NoSuchElementException)
            {
                Console.WriteLine("One or both shareholding pattern tables not found.");
                return null;
            }
            catch (WebDriverTimeoutException)
            {
                Console.WriteLine("Timed out waiting for shareholding pattern tables.");
                return null;
            }
        }

        private static List<Dictionary<string, string>> ParseTable(IWebElement table)
        {
            var data = new List<Dictionary<string, string>>();

            // Get all rows (tr)
            var rows = table.FindElements(By.TagName("tr")).ToList();

            if (rows.Count == 0) return data;

            // Get headers from first row (th)
            var headers = rows[0].FindElements(By.TagName("th"))
                            .Select(h => h.Text.Trim())
                            .ToList();

            // Defensive: If no <th>, try first row's <td>
            if (headers.Count == 0)
            {
                headers = rows[0].FindElements(By.TagName("td"))
                             .Select(td => td.Text.Trim())
                             .ToList();
            }

            // Iterate remaining rows for data
            for (int i = 1; i < rows.Count; i++)
            {
                var cells = rows[i].FindElements(By.TagName("td")).ToList();
                if (cells.Count == 0) continue;

                var rowDict = new Dictionary<string, string>();

                // Map each cell text to corresponding header (or "ColumnX" if no header)
                for (int j = 0; j < cells.Count; j++)
                {
                    string key = j < headers.Count ? headers[j] : $"Column{j + 1}";
                    string value = cells[j].Text.Trim();
                    rowDict[key] = value;
                }

                data.Add(rowDict);
            }

            return data;
        }

        // public static async Task<string?> ScrapeShareholdingPatternAsync(string pageUrl)
        // {
        //     var options = new ChromeOptions();
        //     options.AddArgument("--headless");
        //     options.AddArgument("--no-sandbox");
        //     options.AddArgument("--disable-dev-shm-usage");

        //     using var driver = new ChromeDriver(options);
        //     driver.Navigate().GoToUrl(pageUrl);

        //     try
        //     {
        //         WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
        //         wait.Until(d => d.FindElement(By.CssSelector("#shareholding table")));

        //         var table = driver.FindElement(By.CssSelector("#shareholding table"));

        //         // Extract headers
        //         var headerCells = table.FindElements(By.CssSelector("thead tr th")).Skip(1).ToList(); // Skip first empty/th column
        //         var headers = headerCells.Select(h => h.Text.Trim()).ToList();

        //         var dataRows = table.FindElements(By.CssSelector("tbody tr"));

        //         var resultList = new List<Dictionary<string, string>>();

        //         foreach (var row in dataRows)
        //         {
        //             var cells = row.FindElements(By.TagName("td")).ToList();
        //             if (cells.Count == 0) continue;

        //             var rowDict = new Dictionary<string, string>();

        //             // First cell is the category/label
        //             var category = cells[0].Text.Trim();
        //             rowDict["Category"] = category;

        //             // Remaining cells are the values for each header column
        //             for (int i = 1; i < cells.Count; i++)
        //             {
        //                 string header = i - 1 < headers.Count ? headers[i - 1] : $"Col{i}";
        //                 string value = cells[i].Text.Trim();
        //                 rowDict[header] = value;
        //             }

        //             resultList.Add(rowDict);
        //         }

        //         // Serialize to JSON
        //         string json = JsonConvert.SerializeObject(resultList, Formatting.Indented);
        //         return json;
        //     }
        //     catch (Exception ex)
        //     {
        //         Console.WriteLine("[Error] " + ex.Message);
        //         return null;
        //     }
        // }

        // public static async Task<string?> ScrapeShareholdingPatternAsync(string pageUrl)
        // {
        //     var options = new ChromeOptions();
        //     options.AddArgument("--headless");
        //     options.AddArgument("--no-sandbox");
        //     options.AddArgument("--disable-dev-shm-usage");

        //     using var driver = new ChromeDriver(options);
        //     driver.Navigate().GoToUrl(pageUrl);

        //     try
        //     {
        //         // Wait for the table inside the shareholding-pattern section to load
        //         WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
        //         wait.Until(d => d.FindElement(By.CssSelector("#shareholding-pattern table")));

        //         // Get the table element only
        //         var shareholdingTable = driver.FindElement(By.CssSelector("#shareholding-pattern table"));
        //         string shareholdingHtml = shareholdingTable.GetAttribute("outerHTML");

        //         return shareholdingHtml;
        //     }
        //     catch (NoSuchElementException)
        //     {
        //         Console.WriteLine("[Error] Shareholding table not found.");
        //         return null;
        //     }
        //     catch (WebDriverTimeoutException)
        //     {
        //         Console.WriteLine("[Error] Timed out waiting for shareholding table.");
        //         return null;
        //     }
        //     catch (Exception ex)
        //     {
        //         Console.WriteLine($"[Error] Unexpected error while scraping shareholding pattern: {ex.Message}");
        //         return null;
        //     }
        // }


        // private static string HtmlTableToText(HtmlNode tableNode)
        // {
        //     var rows = tableNode.SelectNodes(".//tr");
        //     if (rows == null) return "";

        //     var lines = new List<string>();

        //     foreach (var row in rows)
        //     {
        //         var cells = row.SelectNodes(".//th|.//td");
        //         if (cells == null) continue;

        //         var line = string.Join(" | ", cells.Select(c => c.InnerText.Trim()));
        //         lines.Add(line);
        //     }

        //     return string.Join("\n", lines);
        // }


    }
}


