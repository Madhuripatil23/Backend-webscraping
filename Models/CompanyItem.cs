namespace webscrapperapi.Models
{
    public class CompanyItem
{
    public int CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public string? Symbol { get; set; }
    public string? ScreenerUrl { get; set; }
    public List<Period> Period { get; set; } = new();
}

    public class Period
    {
        public string ReportId { get; set; } = "";
        public string YM { get; set; } = "";
        public string PPTUrl { get; set; } = "";
        public string TranscriptUrl { get; set; } = "";
        public string SummaryUrl { get; set; } = "";
}
}
