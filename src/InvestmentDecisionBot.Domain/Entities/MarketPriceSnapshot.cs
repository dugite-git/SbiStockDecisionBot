namespace InvestmentDecisionBot.Domain.Entities;

public sealed class MarketPriceSnapshot
{
    public int Id { get; set; }
    public int SecurityId { get; set; }
    public Security Security { get; set; } = null!;
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
    public string DataSource { get; set; } = "";
    public bool IsStale { get; set; }
    public bool UsedFallback { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
