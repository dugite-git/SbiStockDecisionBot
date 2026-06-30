namespace InvestmentDecisionBot.Domain.Entities;

public sealed class ExternalApiCacheEntry
{
    public int Id { get; set; }
    public string Provider { get; set; } = "";
    public string Function { get; set; } = "";
    public string CacheKey { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public DateTimeOffset FetchedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Succeeded { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
