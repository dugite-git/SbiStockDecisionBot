namespace InvestmentDecisionBot.Domain.Entities;

public sealed class ImportBatch
{
    public int Id { get; set; }
    public string? SourceCsvFileName { get; set; }
    public string EncodingName { get; set; } = "";
    public DateTimeOffset ImportedAt { get; set; }
    public int ImportedCount { get; set; }
    public int CreatedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SoldCount { get; set; }
    public int WatchAddedCount { get; set; }
    public int SkippedCount { get; set; }
    public bool Succeeded { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
