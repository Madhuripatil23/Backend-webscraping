using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace webscrapperapi.Helpers
{
    public static class FileDownloader
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private const int MaxPdfRetry = 3;
        private const int TimeoutSeconds = 30;
        private const int DownloadChunkSize = 8192; // 8 KB
        private static readonly byte[] PdfMagic = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // %PDF

        public static async Task DownloadPdfAsync(string url, string targetPath)
        {
            if (File.Exists(targetPath))
            {
                Console.WriteLine($"Already present – skip {targetPath}");
                return;
            }

            for (int attempt = 0; attempt < MaxPdfRetry; attempt++)
            {
                if (attempt > 0)
                {
                    int delay = (int)Math.Pow(2, attempt); // exponential backoff
                    Console.WriteLine($"Retrying in {delay} seconds...");
                    await Task.Delay(delay * 1000);
                }

                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/124.0.0.0");
                    using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    await using var stream = await response.Content.ReadAsStreamAsync();

                    byte[] header = new byte[4];
                    int read = await stream.ReadAsync(header, 0, 4);
                    if (read != 4 || !header.AsSpan().SequenceEqual(PdfMagic))
                    {
                        throw new InvalidDataException("Not a valid PDF file.");
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

                    await using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write);
                    await fileStream.WriteAsync(header, 0, 4); // write the already-read header

                    byte[] buffer = new byte[DownloadChunkSize];
                    int bytesRead;
                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                    }

                    Console.WriteLine($"Saved {targetPath}");
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"PDF retry {attempt + 1}/{MaxPdfRetry} failed ({Path.GetFileName(targetPath)}): {ex.Message}");
                }
            }

            Console.WriteLine($"Permanent failure – {url}");
        }
    }
}
