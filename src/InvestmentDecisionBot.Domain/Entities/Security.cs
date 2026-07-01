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
    public string? ExternalSymbol { get; set; }
    public DateTimeOffset? ExternalSymbolResolvedAt { get; set; }
    public string? ExternalSymbolResolutionError { get; set; }
    public SecurityType SecurityType { get; set; } = SecurityType.Stock;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsJapaneseStock()
    {
        return SecurityType == SecurityType.Stock &&
               Symbol.Length == 4 &&
               Symbol.All(char.IsDigit) &&
               (string.IsNullOrWhiteSpace(Country) || string.Equals(Country, "JP", StringComparison.OrdinalIgnoreCase)) &&
               (string.IsNullOrWhiteSpace(Currency) || string.Equals(Currency, "JPY", StringComparison.OrdinalIgnoreCase));
    }
}
