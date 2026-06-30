using InvestmentDecisionBot.Application.Watchlist;
using InvestmentDecisionBot.Domain.Entities;
using InvestmentDecisionBot.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace InvestmentDecisionBot.Tests.Watchlist;

public sealed class WatchlistServiceTests
{
    [Fact]
    public async Task AddsPreventsDuplicatesAndRemovesWatchlistItems()
    {
        using var db = new TestDb();
        var service = new WatchlistService(db.Context);

        var add = await service.AddAsync("7203", CancellationToken.None);
        var duplicate = await service.AddAsync("7203", CancellationToken.None);
        var list = await service.ListAsync(CancellationToken.None);
        var remove = await service.RemoveAsync("7203", CancellationToken.None);

        Assert.True(add.Succeeded);
        Assert.True(duplicate.Succeeded);
        Assert.Single(list);
        Assert.Equal("7203", list[0].Symbol);
        Assert.True(remove.Succeeded);
        Assert.Empty(await service.ListAsync(CancellationToken.None));

        var security = await db.Context.Securities.SingleAsync(s => s.Symbol == "7203");
        Assert.Equal("JP", security.Country);
        Assert.Equal("JPY", security.Currency);
    }

    [Fact]
    public async Task RejectsNonJapaneseStockSymbols()
    {
        using var db = new TestDb();
        var service = new WatchlistService(db.Context);

        var result = await service.AddAsync("NVDA", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(await db.Context.Securities.ToListAsync());
    }

    [Fact]
    public async Task HoldingRemainsMarkedEvenWhenWatchlistIsRemoved()
    {
        using var db = new TestDb();
        var security = new Security { Symbol = "7203", Name = "Toyota", SecurityType = SecurityType.Stock };
        db.Context.Securities.Add(security);
        db.Context.Holdings.Add(new Holding { Security = security, Quantity = 100, AverageAcquisitionPrice = 1, AcquisitionAmount = 100, IsActive = true });
        await db.Context.SaveChangesAsync();

        var service = new WatchlistService(db.Context);
        await service.AddAsync("7203", CancellationToken.None);
        var list = await service.ListAsync(CancellationToken.None);
        await service.RemoveAsync("7203", CancellationToken.None);

        Assert.True(list[0].IsHolding);
        Assert.True(await db.Context.Holdings.AnyAsync(h => h.IsActive));
    }
}
