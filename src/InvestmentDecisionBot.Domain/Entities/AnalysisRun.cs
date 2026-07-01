namespace InvestmentDecisionBot.Domain.Entities;

public sealed class AnalysisRun
{
    public int Id { get; set; }
    public DateOnly AnalysisDate { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string Trigger { get; set; } = "";
    public int? ImportBatchId { get; set; }
    public ImportBatch? ImportBatch { get; set; }
    public bool Succeeded { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
