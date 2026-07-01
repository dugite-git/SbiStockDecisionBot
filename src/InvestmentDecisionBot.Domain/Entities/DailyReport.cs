namespace InvestmentDecisionBot.Domain.Entities;

public sealed class DailyReport
{
    public int Id { get; set; }
    public int? AnalysisRunId { get; set; }
    public AnalysisRun? AnalysisRun { get; set; }
    public DateOnly ReportDate { get; set; }
    public string Content { get; set; } = "";
    public bool PostedToDiscord { get; set; }
    public string? DiscordMessageId { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
