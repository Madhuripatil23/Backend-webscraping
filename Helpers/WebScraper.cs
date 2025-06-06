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

namespace webscrapperapi.Helpers
{
    public class WebScraper
    {
        private const string XPATH_UL = "/html/body/main/section[10]/div[2]/div[4]/div[2]/ul";
        private const string SHOW_MORE_XPATH = "/html/body/main/section[10]/div[2]/div[4]/div[2]/button";
        private const int TIMEOUT_SEC = 30;
        private const int MAX_SCRAPE_RETRY = 3;
        private const string USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/124.0.0.0";

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

                    var ulElem = ExpandUl(driver, XPATH_UL);
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
            return new List<ScrapeResult>(); // ✅ safe fallback
        }

        private static List<IWebElement> ExpandUl(IWebDriver driver, string ulXPath, int maxWaitSeconds = 10)
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
                        var showMore = driver.FindElement(By.XPath(SHOW_MORE_XPATH));
                        if (showMore.Displayed && showMore.Enabled)
                        {
                            showMore.Click();
                            Thread.Sleep(700);
                        }
                    }
                    catch (NoSuchElementException)
                    {
                        return lis;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("ExpandUl inner click error: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ExpandUl failed: " + ex.Message);
                return new List<IWebElement>(); // return safe empty list
            }
        }
    }
}
