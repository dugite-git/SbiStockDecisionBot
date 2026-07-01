using InvestmentDecisionBot.Domain.Entities;
using InvestmentDecisionBot.Domain.Enums;

namespace InvestmentDecisionBot.Application.Abstractions;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ISecurityRepository
{
    Task<Security?> FindBySymbolAsync(SecurityType securityType, string symbol, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, Security>> FindBySymbolsAsync(SecurityType securityType, IReadOnlyCollection<string> symbols, CancellationToken cancellationToken);
    Task<IReadOnlyList<Security>> ListByIdsAsync(IReadOnlyCollection<int> securityIds, CancellationToken cancellationToken);
    void Add(Security security);
}

public interface IHoldingRepository
{
    Task<Holding?> FindByStockSymbolAsync(string symbol, CancellationToken cancellationToken);
    Task<IReadOnlyList<Holding>> ListActiveWithSecurityAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<int>> ListActiveSecurityIdsAsync(CancellationToken cancellationToken);
    void Add(Holding holding);
}

public interface IHoldingSnapshotRepository
{
    void Add(HoldingSnapshot snapshot);
}

public interface IWatchlistRepository
{
    Task<WatchlistItem?> FindActiveBySecurityIdAsync(int securityId, CancellationToken cancellationToken);
    Task<WatchlistItem?> FindActiveBySymbolWithSecurityAsync(string symbol, CancellationToken cancellationToken);
    Task<IReadOnlyList<WatchlistItem>> ListActiveWithSecurityAsync(CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<int, WatchlistSource>> ListActiveSecuritySourcesAsync(CancellationToken cancellationToken);
    void Add(WatchlistItem item);
}

public interface ISoldEventRepository
{
    void Add(SoldEvent soldEvent);
}

public interface IMarketPriceSnapshotRepository
{
    Task<MarketPriceSnapshot?> FindReusableTodayAsync(int securityId, CancellationToken cancellationToken);
    Task<bool> HasReusableTodayAsync(int securityId, CancellationToken cancellationToken);
    void Add(MarketPriceSnapshot snapshot);
}

public interface IExternalApiCacheRepository
{
    Task<bool> HasSuccessfulNewsFetchAsync(string symbol, CancellationToken cancellationToken);
}

public interface IAnalysisResultRepository
{
    void Add(AnalysisResult analysisResult);
}

public interface IDailyReportRepository
{
    void Add(DailyReport dailyReport);
}
