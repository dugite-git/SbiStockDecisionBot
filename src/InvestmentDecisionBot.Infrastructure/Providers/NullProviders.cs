using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Application.DTOs;
using InvestmentDecisionBot.Domain.Entities;
using InvestmentDecisionBot.Domain.Enums;

namespace InvestmentDecisionBot.Infrastructure.Providers;

public sealed class NullMarketDataProvider : IMarketDataProvider, ICachedMarketDataProvider, IMarketDataPrefetchService
{
    public Task<MarketPriceResult> GetLatestPriceAsync(Security security, CancellationToken cancellationToken) =>
        Task.FromResult(new MarketPriceResult(null, security.Currency, true, true, "市場データ取得Providerは無効です。"));

    public Task<IReadOnlyList<DailyPriceBar>> GetCachedDailyPricesAsync(Security security, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DailyPriceBar>>(Array.Empty<DailyPriceBar>());

    public Task<IReadOnlyList<NewsSentimentData>> GetCachedNewsAsync(Security security, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NewsSentimentData>>(Array.Empty<NewsSentimentData>());

    public Task<decimal?> GetCachedExchangeRateAsync(string fromCurrency, string toCurrency, CancellationToken cancellationToken) =>
        Task.FromResult<decimal?>(null);

    public Task<MarketDataStatusResult> GetStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new MarketDataStatusResult(0, 0, 0, 0, Array.Empty<string>()));

    public Task<MarketDataPrefetchResult> PrefetchAsync(int? limit, CancellationToken cancellationToken) =>
        Task.FromResult(new MarketDataPrefetchResult(
            limit ?? 0,
            0,
            0,
            0,
            0,
            0,
            ["市場データ取得Providerは無効です。"],
            Array.Empty<MarketDataRequestLogItem>()));

    public Task<MarketDataCoverageResult> GetCoverageAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new MarketDataCoverageResult(0, 0, 0, 0, 0, 0, Array.Empty<MarketDataCoverageItem>()));

    public Task<MarketDataDetailResult> GetDetailAsync(string symbol, CancellationToken cancellationToken) =>
        Task.FromResult(new MarketDataDetailResult(
            false,
            symbol,
            null,
            Array.Empty<DailyPriceBar>(),
            null,
            Array.Empty<NewsSentimentData>(),
            Array.Empty<ExternalApiCacheSummary>(),
            "Market data provider is disabled."));
}

public sealed class NullNewsProvider : INewsProvider
{
    public Task<IReadOnlyList<NewsItem>> GetNewsAsync(Security security, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NewsItem>>(Array.Empty<NewsItem>());
}

public sealed class NullFinancialDataProvider : IFinancialDataProvider
{
    public Task<FinancialDataResult> GetFinancialDataAsync(Security security, CancellationToken cancellationToken) =>
        Task.FromResult(new FinancialDataResult(false));
}

public sealed class NullExchangeRateProvider : IExchangeRateProvider
{
    public Task<decimal?> GetRateAsync(string fromCurrency, string toCurrency, CancellationToken cancellationToken) =>
        Task.FromResult<decimal?>(null);
}

public sealed class RuleOnlyDiscordPublisher : IDiscordReportPublisher
{
    public Task<DiscordPostResult> PostReportAsync(string content, CancellationToken cancellationToken) =>
        Task.FromResult(new DiscordPostResult(false, null, "Discord publisher is not connected in this process."));
}
