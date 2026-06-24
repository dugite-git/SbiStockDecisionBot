namespace InvestmentDecisionBot.Domain.Entities;

public sealed class AiAnalysisLog
{
    public int Id { get; set; }
    public int? AnalysisResultId { get; set; }
    public AnalysisResult? AnalysisResult { get; set; }
    public string RequestJson { get; set; } = "";
    public string? ResponseJson { get; set; }
    public string Model { get; set; } = "";
    public bool Succeeded { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
