using InvestmentDecisionBot.Domain.Enums;

namespace InvestmentDecisionBot.Application.DTOs;

public sealed record ReportResult(
    bool Succeeded,
    string Content,
    int AnalysisCount,
    string? DiscordMessageId,
    string? ErrorMessage,
    int? AnalysisRunId,
    int? SourceImportBatchId,
    string? SourceCsvFileName,
    DateTimeOffset? SourceImportedAt,
    IReadOnlyList<ReportDecisionSummaryItem>? ImportantItems = null,
    IReadOnlyList<ReportDecisionSummaryItem>? Items = null,
    ReportDecisionCounts? DecisionCounts = null,
    IReadOnlyList<string>? MissingDataCategories = null);

public sealed record ReportDecisionSummaryItem(
    string Symbol,
    string Name,
    TargetType TargetType,
    BotDecision Decision,
    decimal TotalScore,
    decimal? UnrealizedProfitLossRate,
    decimal Confidence,
    string Reason,
    decimal FundamentalScore,
    decimal QualityScore,
    decimal MomentumScore,
    decimal NewsScore,
    decimal PositionRiskScore,
    IReadOnlyList<string> MissingData,
    IReadOnlyList<string> Warnings);

public sealed record ReportDecisionCounts(
    int TakeProfit,
    int PartialTakeProfit,
    int StopLoss,
    int PartialStopLoss,
    int NewBuy,
    int Hold,
    int ImportantTotal);

public sealed record DiscordPostResult(bool Succeeded, string? MessageId, string? ErrorMessage);
