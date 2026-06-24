using InvestmentDecisionBot.Domain.Enums;

namespace InvestmentDecisionBot.Domain.Entities;

public sealed class AnalysisResult
{
    public int Id { get; set; }
    public int SecurityId { get; set; }
    public Security Security { get; set; } = null!;
    public DateOnly AnalysisDate { get; set; }
    public TargetType TargetType { get; set; }
    public decimal FundamentalScore { get; set; }
    public decimal QualityScore { get; set; }
    public decimal MomentumScore { get; set; }
    public decimal NewsScore { get; set; }
    public decimal PositionRiskScore { get; set; }
    public decimal TotalScore { get; set; }
    public BotDecision BotDecision { get; set; }
    public SellReasonType SellReasonType { get; set; }
    public decimal Confidence { get; set; }
    public string Reason { get; set; } = "";
    public string? MissingData { get; set; }
    public bool DecisionConflict { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
