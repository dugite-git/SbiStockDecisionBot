using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Domain.Entities;
using InvestmentDecisionBot.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace InvestmentDecisionBot.Infrastructure.Persistence.Repositories;

public sealed class EfUnitOfWork(BotDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed class EfSecurityRepository(BotDbContext db) : ISecurityRepository
{
    public Task<Security?> FindBySymbolAsync(SecurityType securityType, string symbol, CancellationToken cancellationToken)
    {
        return db.Securities.FirstOrDefaultAsync(
            security => security.SecurityType == securityType && security.Symbol == symbol,
            cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, Security>> FindBySymbolsAsync(
        SecurityType securityType,
        IReadOnlyCollection<string> symbols,
        CancellationToken cancellationToken)
    {
        return await db.Securities
            .Where(security => security.SecurityType == securityType && symbols.Contains(security.Symbol))
            .ToDictionaryAsync(security => security.Symbol, StringComparer.OrdinalIgnoreCase, cancellationToken);
    }

    public async Task<IReadOnlyList<Security>> ListByIdsAsync(IReadOnlyCollection<int> securityIds, CancellationToken cancellationToken)
    {
        return await db.Securities
            .Where(security => securityIds.Contains(security.Id))
            .OrderBy(security => security.Symbol)
            .ToListAsync(cancellationToken);
    }

    public void Add(Security security)
    {
        db.Securities.Add(security);
    }
}

public sealed class EfHoldingRepository(BotDbContext db) : IHoldingRepository
{
    public Task<Holding?> FindByStockSymbolWithSecurityAsync(string symbol, CancellationToken cancellationToken)
    {
        return db.Holdings
            .Include(holding => holding.Security)
            .FirstOrDefaultAsync(
                holding => holding.Security.Symbol == symbol && holding.Security.SecurityType == SecurityType.Stock,
                cancellationToken);
    }

    public async Task<IReadOnlyList<Holding>> ListActiveWithSecurityAsync(CancellationToken cancellationToken)
    {
        return await db.Holdings
            .Include(holding => holding.Security)
            .Where(holding => holding.IsActive)
            .OrderBy(holding => holding.Security.Symbol)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<int>> ListActiveSecurityIdsAsync(CancellationToken cancellationToken)
    {
        return await db.Holdings
            .Where(holding => holding.IsActive)
            .Select(holding => holding.SecurityId)
            .ToListAsync(cancellationToken);
    }

    public void Add(Holding holding)
    {
        db.Holdings.Add(holding);
    }
}

public sealed class EfHoldingSnapshotRepository(BotDbContext db) : IHoldingSnapshotRepository
{
    public void Add(HoldingSnapshot snapshot)
    {
        db.HoldingSnapshots.Add(snapshot);
    }
}

public sealed class EfWatchlistRepository(BotDbContext db) : IWatchlistRepository
{
    public Task<WatchlistItem?> FindActiveBySecurityIdAsync(int securityId, CancellationToken cancellationToken)
    {
        return db.WatchlistItems.FirstOrDefaultAsync(
            item => item.SecurityId == securityId && item.IsActive,
            cancellationToken);
    }

    public Task<WatchlistItem?> FindActiveBySymbolWithSecurityAsync(string symbol, CancellationToken cancellationToken)
    {
        return db.WatchlistItems
            .Include(item => item.Security)
            .FirstOrDefaultAsync(item => item.Security.Symbol == symbol && item.IsActive, cancellationToken);
    }

    public async Task<IReadOnlyList<WatchlistItem>> ListActiveWithSecurityAsync(CancellationToken cancellationToken)
    {
        return await db.WatchlistItems
            .Include(item => item.Security)
            .Where(item => item.IsActive)
            .OrderBy(item => item.Security.Symbol)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<int, WatchlistSource>> ListActiveSecuritySourcesAsync(CancellationToken cancellationToken)
    {
        var items = await db.WatchlistItems
            .Where(item => item.IsActive)
            .Select(item => new { item.SecurityId, item.Source })
            .ToListAsync(cancellationToken);

        return items
            .GroupBy(item => item.SecurityId)
            .ToDictionary(group => group.Key, group => group.First().Source);
    }

    public void Add(WatchlistItem item)
    {
        db.WatchlistItems.Add(item);
    }
}

public sealed class EfSoldEventRepository(BotDbContext db) : ISoldEventRepository
{
    public void Add(SoldEvent soldEvent)
    {
        db.SoldEvents.Add(soldEvent);
    }
}

public sealed class EfMarketPriceSnapshotRepository(BotDbContext db) : IMarketPriceSnapshotRepository
{
    public Task<MarketPriceSnapshot?> FindReusableTodayAsync(int securityId, CancellationToken cancellationToken)
    {
        var (today, tomorrow) = GetUtcDayRange();

        return db.MarketPriceSnapshots
            .Where(snapshot =>
                snapshot.SecurityId == securityId &&
                snapshot.Price != null &&
                !snapshot.UsedFallback &&
                snapshot.FetchedAt >= today &&
                snapshot.FetchedAt < tomorrow)
            .OrderByDescending(snapshot => snapshot.FetchedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<bool> HasReusableTodayAsync(int securityId, CancellationToken cancellationToken)
    {
        var (today, tomorrow) = GetUtcDayRange();

        return db.MarketPriceSnapshots.AnyAsync(
            snapshot =>
                snapshot.SecurityId == securityId &&
                snapshot.Price != null &&
                !snapshot.UsedFallback &&
                snapshot.FetchedAt >= today &&
                snapshot.FetchedAt < tomorrow,
            cancellationToken);
    }

    public void Add(MarketPriceSnapshot snapshot)
    {
        db.MarketPriceSnapshots.Add(snapshot);
    }

    private static (DateTimeOffset Today, DateTimeOffset Tomorrow) GetUtcDayRange()
    {
        var today = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);
        return (today, today.AddDays(1));
    }
}

public sealed class EfExternalApiCacheRepository(BotDbContext db) : IExternalApiCacheRepository
{
    public Task<bool> HasSuccessfulNewsFetchAsync(string symbol, CancellationToken cancellationToken)
    {
        return db.ExternalApiCacheEntries.AnyAsync(
            cache =>
                cache.Provider == "Gdelt" &&
                cache.Function == "ArticleList" &&
                cache.CacheKey == symbol &&
                cache.Succeeded,
            cancellationToken);
    }
}

public sealed class EfAnalysisResultRepository(BotDbContext db) : IAnalysisResultRepository
{
    public void Add(AnalysisResult analysisResult)
    {
        db.AnalysisResults.Add(analysisResult);
    }
}

public sealed class EfDailyReportRepository(BotDbContext db) : IDailyReportRepository
{
    public void Add(DailyReport dailyReport)
    {
        db.DailyReports.Add(dailyReport);
    }
}
