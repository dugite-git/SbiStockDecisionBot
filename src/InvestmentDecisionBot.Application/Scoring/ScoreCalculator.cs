using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Application.DTOs;
using InvestmentDecisionBot.Domain.Enums;

namespace InvestmentDecisionBot.Application.Scoring;

public sealed class ScoreCalculator : IScoreCalculator
{
    public ScoreResult Calculate(AnalysisInput input)
    {
        var missingData = input.MissingData.ToList();
        var fundamental = 50m;
        var quality = 50m;
        var momentum = 50m;
        var news = 50m;
        var positionRisk = input.TargetType == TargetType.Holding
            ? CalculatePositionRisk(input)
            : 50m;

        if (!missingData.Contains("fundamental")) missingData.Add("fundamental");
        if (!missingData.Contains("quality")) missingData.Add("quality");
        if (!missingData.Contains("momentum")) missingData.Add("momentum");
        if (!missingData.Contains("news")) missingData.Add("news");

        var total = fundamental * 0.40m + quality * 0.20m + momentum * 0.20m + news * 0.10m + positionRisk * 0.10m;
        var rate = CalculateProfitLossRate(input);

        return new ScoreResult(fundamental, quality, momentum, news, positionRisk, decimal.Round(total, 2), rate, missingData);
    }

    private static decimal CalculatePositionRisk(AnalysisInput input)
    {
        var rate = CalculateProfitLossRate(input);
        if (rate is null)
        {
            return 50m;
        }

        if (rate >= 40m) return 45m;
        if (rate >= 25m) return 55m;
        if (rate <= -20m) return 20m;
        if (rate <= -10m) return 35m;
        return 65m;
    }

    private static decimal? CalculateProfitLossRate(AnalysisInput input)
    {
        if (input.MarketValue is > 0m && input.UnrealizedProfitLoss is not null)
        {
            var invested = input.MarketValue.Value - input.UnrealizedProfitLoss.Value;
            if (invested > 0m)
            {
                return decimal.Round(input.UnrealizedProfitLoss.Value / invested * 100m, 2);
            }
        }

        if (input.AverageAcquisitionPrice is > 0m && input.CurrentPrice is not null)
        {
            return decimal.Round((input.CurrentPrice.Value - input.AverageAcquisitionPrice.Value) / input.AverageAcquisitionPrice.Value * 100m, 2);
        }

        return null;
    }
}
