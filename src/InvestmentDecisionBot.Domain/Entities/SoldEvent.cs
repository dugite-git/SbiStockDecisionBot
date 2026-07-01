namespace InvestmentDecisionBot.Domain.Entities;

public sealed class SoldEvent
{
    public int Id { get; set; }
    public int SecurityId { get; set; }
    public Security Security { get; set; } = null!;
    public int? ImportBatchId { get; set; }
    public ImportBatch? ImportBatch { get; set; }
    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;
    public decimal PreviousQuantity { get; set; }
    public decimal PreviousAverageAcquisitionPrice { get; set; }
    public decimal? PreviousImportedCurrentPrice { get; set; }
    public decimal? PreviousImportedMarketValue { get; set; }
    public decimal? PreviousImportedUnrealizedProfitLoss { get; set; }
    public string Reason { get; set; } = "MissingFromLatestSbiCsv";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
