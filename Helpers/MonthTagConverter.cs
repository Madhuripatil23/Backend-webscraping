// Helpers/MonthTagConverter.cs
using System;
using System.Globalization;

namespace webscrapperapi.Helpers
{
    public static class MonthTagConverter
    {
        /// <summary>
        /// Converts a date tag like "Jan-2023" or "January 2023" into "yyyy-MM" format,
        /// e.g. "2023-01". Returns "unknown" if parsing fails or input is empty.
        /// </summary>
        /// <param name="monthTag">Input date tag string</param>
        /// <returns>Formatted year-month string or "unknown"</returns>
        public static string ToYearMonth(string monthTag)
        {
            if (string.IsNullOrWhiteSpace(monthTag) || monthTag.ToLower() == "unknown")
                return "unknown";

            try
            {
                // Try parsing formats like "Jan-2023" or "January 2023"
                var date = DateTime.ParseExact(
                    monthTag,
                    new[] { "MMM yyyy", "MMMM yyyy" },
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None);

                return date.ToString("yyyy-MM");
            }
            catch
            {
                // Return "unknown" if parsing fails
                return "unknown";
            }
        }
    }
}
