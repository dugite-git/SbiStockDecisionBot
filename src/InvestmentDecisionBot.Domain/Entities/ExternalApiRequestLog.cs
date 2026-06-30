namespace InvestmentDecisionBot.Domain.Entities;

public sealed class ExternalApiRequestLog
{
    public int Id { get; set; }
    public string Provider { get; set; } = "";
    public string Function { get; set; } = "";
    public string CacheKey { get; set; } = "";
    public DateTimeOffset RequestedAt { get; set; }
    public bool Succeeded { get; set; }
    public string? ErrorMessage { get; set; }
}
