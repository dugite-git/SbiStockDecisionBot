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

        var add = await service.AddAsync("nvda", CancellationToken.None);
        var duplicate = await service.AddAsync("NVDA", CancellationToken.None);
        var list = await service.ListAsync(CancellationToken.None);
        var remove = await service.RemoveAsync("NVDA", CancellationToken.None);

        Assert.True(add.Succeeded);
        Assert.True(duplicate.Succeeded);
        Assert.Single(list);
        Assert.Equal("NVDA", list[0].Symbol);
        Assert.True(remove.Succeeded);
        Assert.Empty(await service.ListAsync(CancellationToken.None));
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
