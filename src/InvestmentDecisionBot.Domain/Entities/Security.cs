using InvestmentDecisionBot.Domain.Enums;

namespace InvestmentDecisionBot.Domain.Entities;

public sealed class Security
{
    public int Id { get; set; }
    public string Symbol { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Market { get; set; }
    public string? Country { get; set; }
    public string? Currency { get; set; }
    public string? AlphaVantageSymbol { get; set; }
    public DateTimeOffset? AlphaVantageSymbolResolvedAt { get; set; }
    public string? AlphaVantageSymbolResolutionError { get; set; }
    public SecurityType SecurityType { get; set; } = SecurityType.Stock;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
