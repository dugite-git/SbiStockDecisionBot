using InvestmentDecisionBot.Domain.Enums;

namespace InvestmentDecisionBot.Application.DTOs;

public sealed record AnalysisInput(
    int SecurityId,
    string Symbol,
    string Name,
    TargetType TargetType,
    decimal? Quantity,
    decimal? AverageAcquisitionPrice,
    decimal? CurrentPrice,
    decimal? MarketValue,
    decimal? UnrealizedProfitLoss,
    IReadOnlyList<string> MissingData,
    IReadOnlyList<DailyPriceBar>? DailyPrices = null,
    IReadOnlyList<NewsSentimentData>? News = null,
    FinancialSnapshotData? FinancialSnapshot = null,
    decimal? TotalPortfolioMarketValue = null,
    string? Currency = null,
    IReadOnlyList<string>? CacheWarnings = null);

public sealed record ScoreResult(
    decimal FundamentalScore,
    decimal QualityScore,
    decimal MomentumScore,
    decimal NewsScore,
    decimal PositionRiskScore,
    decimal TotalScore,
    decimal? UnrealizedProfitLossRate,
    IReadOnlyList<string> MissingData,
    ScoreBreakdown? Fundamental = null,
    ScoreBreakdown? Quality = null,
    ScoreBreakdown? Momentum = null,
    ScoreBreakdown? News = null,
    ScoreBreakdown? PositionRisk = null,
    IReadOnlyList<string>? Reasons = null,
    IReadOnlyList<string>? Warnings = null);

public sealed record ScoreBreakdown(decimal RawScore, decimal Confidence, decimal AdjustedScore, IReadOnlyList<string> Reasons, IReadOnlyList<string> Warnings);

public sealed record DailyPriceBar(DateOnly Date, decimal Open, decimal High, decimal Low, decimal Close, long Volume);

public sealed record NewsSentimentData(
    string Title,
    string? Url,
    DateTimeOffset? PublishedAt,
    decimal SentimentScore,
    decimal RelevanceScore,
    string? Summary,
    string? Source = null,
    DateTimeOffset? FetchedAt = null);

public sealed record FinancialSnapshotData(
    DateOnly? DisclosureDate,
    decimal? NetSales,
    decimal? OperatingProfit,
    decimal? OrdinaryProfit,
    decimal? Profit,
    decimal? Eps,
    decimal? Bps,
    decimal? TotalAssets,
    decimal? NetAssets,
    decimal? EquityRatio);

public sealed record MarketDataStatusResult(int DailyLimit, int UsedToday, int RemainingToday, int PendingCount, IReadOnlyList<string> NextItems);

public sealed record MarketDataPrefetchResult(
    int RequestedLimit,
    int Attempted,
    int Succeeded,
    int Skipped,
    int UsedToday,
    int RemainingToday,
    IReadOnlyList<string> Messages,
    IReadOnlyList<MarketDataRequestLogItem> RequestLogs);

public sealed record MarketDataRequestLogItem(
    DateTimeOffset RequestedAt,
    string Function,
    string CacheKey,
    bool Succeeded,
    string? ErrorMessage,
    bool IsCacheHit = false);

public sealed record MarketDataCoverageResult(
    int TargetCount,
    int ProviderCoveredCount,
    int PriceCachedCount,
    int DailyCachedCount,
    int NewsCachedCount,
    int ExchangeRateCachedCount,
    IReadOnlyList<MarketDataCoverageItem> Items);

public sealed record MarketDataCoverageItem(
    string Symbol,
    string Name,
    string TargetType,
    string? ExternalSymbol,
    bool IsProviderCovered,
    bool HasFreshPrice,
    bool HasDailySeries,
    bool HasNewsSentiment,
    bool HasExchangeRate,
    DateTimeOffset? LatestPriceFetchedAt,
    DateTimeOffset? DailyFetchedAt,
    DateTimeOffset? NewsFetchedAt,
    string? ResolutionError);

public sealed record MarketDataDetailResult(
    bool Found,
    string Symbol,
    string? Name,
    IReadOnlyList<DailyPriceBar> DailyPrices,
    FinancialSnapshotData? FinancialSnapshot,
    IReadOnlyList<NewsSentimentData> News,
    IReadOnlyList<ExternalApiCacheSummary> CacheEntries,
    string? Message);

public sealed record ExternalApiCacheSummary(
    string Provider,
    string Function,
    string CacheKey,
    DateTimeOffset FetchedAt,
    DateTimeOffset ExpiresAt,
    bool Succeeded,
    string? ErrorMessage,
    int PayloadLength);

public sealed record DecisionResult(BotDecision Decision, SellReasonType SellReasonType, string Reason, decimal Confidence);

public sealed record MarketPriceResult(decimal? Price, string? Currency, bool UsedFallback, bool IsStale, string? ErrorMessage);

public sealed record FinancialDataResult(bool HasData, FinancialSnapshotData? Snapshot = null, string? ErrorMessage = null);
