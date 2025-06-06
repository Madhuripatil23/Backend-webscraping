// using System.Collections.Generic;
// using System.IO;
// using webscrapperapi.Models;

// public static class CsvReader
// {
//     public static List<CompanyItem> ReadCompanyItemsFromCsv(string filePath)
//     {
//         var companies = new List<CompanyItem>();

//         using var reader = new StreamReader(filePath);
//         string? line;
//         bool isHeader = true;

//         while ((line = reader.ReadLine()) != null)
//         {
//             if (isHeader)
//             {
//                 isHeader = false;
//                 continue; // skip header row
//             }

//             var parts = line.Split(',');
//             if (parts.Length >= 7)  // 7 columns expected
//             {
//                 companies.Add(new CompanyItem
//                 {
//                     CompanyId = int.Parse(parts[0].Trim()),            // CompanyId
//                     CompanyName = parts[1].Trim(),                      // CompanyName
//                     ScreenerUrl = parts[2].Trim(),                      // ScreenerUrl
//                     YM = parts[3].Trim(),                               // YM
//                     PPTUrl = parts[4].Trim(),                           // PPTUrl
//                     TranscriptUrl = parts[5].Trim(),                     // TranscriptUrl
//                     Symbol = parts[6].Trim()                            // Symbol
//                 });
//             }
//         }

//         return companies;
//     }
// }
