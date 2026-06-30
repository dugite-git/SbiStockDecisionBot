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
    decimal? TotalPortfolioMarketValue = null,
    string? Currency = null,
    decimal? UsdJpyRate = null,
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
    string? Summary);

public sealed record MarketDataStatusResult(int DailyLimit, int UsedToday, int RemainingToday, int PendingCount, IReadOnlyList<string> NextItems);

public sealed record MarketDataPrefetchResult(int RequestedLimit, int Attempted, int Succeeded, int Skipped, int UsedToday, int RemainingToday, IReadOnlyList<string> Messages);

public sealed record MarketDataCoverageResult(
    int TargetCount,
    int AlphaVantageCoveredCount,
    int PriceCachedCount,
    int DailyCachedCount,
    int NewsCachedCount,
    int ExchangeRateCachedCount,
    IReadOnlyList<MarketDataCoverageItem> Items);

public sealed record MarketDataCoverageItem(
    string Symbol,
    string Name,
    string TargetType,
    string? AlphaVantageSymbol,
    bool IsAlphaVantageCovered,
    bool HasFreshPrice,
    bool HasDailySeries,
    bool HasNewsSentiment,
    bool HasExchangeRate,
    DateTimeOffset? LatestPriceFetchedAt,
    DateTimeOffset? DailyFetchedAt,
    DateTimeOffset? NewsFetchedAt,
    string? ResolutionError);

public sealed record DecisionResult(BotDecision Decision, SellReasonType SellReasonType, string Reason, decimal Confidence);

public sealed record MarketPriceResult(decimal? Price, string? Currency, bool UsedFallback, bool IsStale, string? ErrorMessage);

public sealed record FinancialDataResult(bool HasData);

public sealed record AiAnalysisRequestDto(
    string Symbol,
    string Name,
    TargetType TargetType,
    ScoreResult Score,
    DecisionResult BotDecision,
    IReadOnlyList<string> NewsSummaries,
    IReadOnlyList<string> MissingData);

public sealed record AiAnalysisResultDto(
    bool Succeeded,
    BotDecision? Decision,
    SellReasonType? SellReasonType,
    decimal Confidence,
    string Reason,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> WatchPoints,
    string? RawJson,
    string? ErrorMessage);
