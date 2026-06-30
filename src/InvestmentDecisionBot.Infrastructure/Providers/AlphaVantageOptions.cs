namespace InvestmentDecisionBot.Infrastructure.Providers;

public sealed class AlphaVantageOptions
{
    public bool Enabled { get; set; }
    public bool FetchOnReport { get; set; }
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://www.alphavantage.co/query";
    public int DailyRequestLimit { get; set; } = 25;
    public int MinimumRequestIntervalMilliseconds { get; set; } = 1000;
    public int FailureCacheMinutes { get; set; } = 30;
}
