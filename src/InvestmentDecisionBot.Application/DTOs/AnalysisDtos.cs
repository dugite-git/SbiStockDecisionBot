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
    IReadOnlyList<string> MissingData);

public sealed record ScoreResult(
    decimal FundamentalScore,
    decimal QualityScore,
    decimal MomentumScore,
    decimal NewsScore,
    decimal PositionRiskScore,
    decimal TotalScore,
    decimal? UnrealizedProfitLossRate,
    IReadOnlyList<string> MissingData);

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
