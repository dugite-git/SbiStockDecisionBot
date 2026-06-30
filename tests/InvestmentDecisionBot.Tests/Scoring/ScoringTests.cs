using InvestmentDecisionBot.Application.DTOs;
using InvestmentDecisionBot.Application.Scoring;
using InvestmentDecisionBot.Domain.Enums;

namespace InvestmentDecisionBot.Tests.Scoring;

public sealed class ScoringTests
{
    [Fact]
    public void UsesNeutralScoresForMissingDataAndCalculatesProfitRate()
    {
        var input = new AnalysisInput(1, "7203", "Toyota", TargetType.Holding, 100, 2000, 2500, 250000, 50000, []);

        var score = new ScoreCalculator().Calculate(input);

        Assert.Equal(50.58m, score.TotalScore);
        Assert.Equal(25m, score.UnrealizedProfitLossRate);
        Assert.Contains("news", score.MissingData);
    }

    [Fact]
    public void ResolvesStopLossBeforeHold()
    {
        var input = new AnalysisInput(1, "7203", "Toyota", TargetType.Holding, 100, 100, 70, 7000, -3000, []);
        var score = new ScoreResult(30, 30, 30, 50, 20, 32, -30, []);

        var decision = new BotDecisionResolver().Resolve(input, score);

        Assert.Equal(BotDecision.StopLoss, decision.Decision);
        Assert.Equal(SellReasonType.StopLoss, decision.SellReasonType);
    }
}
