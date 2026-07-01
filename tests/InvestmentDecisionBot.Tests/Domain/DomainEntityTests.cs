using InvestmentDecisionBot.Domain.Entities;
using InvestmentDecisionBot.Domain.Enums;

namespace InvestmentDecisionBot.Tests.Domain;

public sealed class DomainEntityTests
{
    [Theory]
    [InlineData("7203", SecurityType.Stock, "JP", "JPY", true)]
    [InlineData("7203", SecurityType.Stock, null, "JPY", true)]
    [InlineData("7203", SecurityType.Stock, "JP", null, true)]
    [InlineData("NVDA", SecurityType.Stock, "US", "USD", false)]
    [InlineData("7203", SecurityType.Unknown, "JP", "JPY", false)]
    public void SecurityDetectsJapaneseStocks(string symbol, SecurityType securityType, string? country, string? currency, bool expected)
    {
        var security = new Security
        {
            Symbol = symbol,
            SecurityType = securityType,
            Country = country,
            Currency = currency
        };

        Assert.Equal(expected, security.IsJapaneseStock());
    }

    [Fact]
    public void HoldingUpdatesImportedDataAndMarksActive()
    {
        var importedAt = DateTimeOffset.Parse("2026-07-01T00:00:00Z");
        var holding = new Holding { IsActive = false };

        holding.UpdateFromImportedData(
            100m,
            5m,
            2000m,
            200000m,
            2500m,
            250000m,
            50000m,
            importedAt);

        Assert.Equal(100m, holding.Quantity);
        Assert.Equal(5m, holding.PendingSellQuantity);
        Assert.Equal(2000m, holding.AverageAcquisitionPrice);
        Assert.Equal(200000m, holding.AcquisitionAmount);
        Assert.Equal(2500m, holding.ImportedCurrentPrice);
        Assert.Equal(250000m, holding.ImportedMarketValue);
        Assert.Equal(50000m, holding.ImportedUnrealizedProfitLoss);
        Assert.Equal(importedAt, holding.ImportedAt);
        Assert.True(holding.IsActive);
        Assert.Equal(importedAt, holding.UpdatedAt);
    }

    [Fact]
    public void HoldingCanBeMarkedAsSold()
    {
        var soldAt = DateTimeOffset.Parse("2026-07-01T01:00:00Z");
        var holding = new Holding { IsActive = true };

        holding.MarkAsSold(soldAt);

        Assert.False(holding.IsActive);
        Assert.Equal(soldAt, holding.UpdatedAt);
    }

    [Fact]
    public void WatchlistItemCanBeDeactivated()
    {
        var now = DateTimeOffset.Parse("2026-07-01T02:00:00Z");
        var item = new WatchlistItem { IsActive = true };

        item.Deactivate(now);

        Assert.False(item.IsActive);
        Assert.Equal(now, item.RemovedAt);
        Assert.Equal(now, item.UpdatedAt);
    }
}
