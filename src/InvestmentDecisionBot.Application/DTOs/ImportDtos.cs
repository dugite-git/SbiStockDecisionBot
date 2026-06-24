namespace InvestmentDecisionBot.Application.DTOs;

public sealed record ParsedSbiHolding(
    string Symbol,
    string Name,
    decimal Quantity,
    decimal? PendingSellQuantity,
    decimal AverageAcquisitionPrice,
    decimal? ImportedCurrentPrice,
    decimal AcquisitionAmount,
    decimal? ImportedMarketValue,
    decimal? ImportedUnrealizedProfitLoss);

public sealed record SbiCsvParseResult(
    IReadOnlyList<ParsedSbiHolding> Holdings,
    int SkippedInvestmentTrustSections,
    int SkippedSummaryRows,
    string EncodingName);

public sealed record SbiImportResult(
    bool Succeeded,
    string Message,
    int ImportedCount,
    int CreatedCount,
    int UpdatedCount,
    int SoldDetectedCount,
    int WatchlistAddedCount,
    string EncodingName);
