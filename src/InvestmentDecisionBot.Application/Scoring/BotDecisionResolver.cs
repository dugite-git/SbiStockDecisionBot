using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Application.DTOs;
using InvestmentDecisionBot.Domain.Enums;

namespace InvestmentDecisionBot.Application.Scoring;

public sealed class BotDecisionResolver : IBotDecisionResolver
{
    public DecisionResult Resolve(AnalysisInput input, ScoreResult score)
    {
        if (input.TargetType == TargetType.Watchlist)
        {
            return score.TotalScore >= 70m
                ? new DecisionResult(BotDecision.NewBuy, SellReasonType.None, "参考候補として監視継続価値があります。", 0.65m)
                : new DecisionResult(BotDecision.Skip, SellReasonType.None, "現時点では監視継続にとどめます。", 0.65m);
        }

        var rate = score.UnrealizedProfitLossRate;
        if (rate >= 40m && score.TotalScore < 55m)
        {
            return new DecisionResult(BotDecision.TakeProfit, SellReasonType.TakeProfit, "含み益が大きく、総合スコアが弱いため利益確定の参考警戒です。", 0.7m);
        }

        if (rate >= 25m && score.MomentumScore < 50m)
        {
            return new DecisionResult(BotDecision.PartialTakeProfit, SellReasonType.TakeProfit, "含み益があり、勢いが中立未満のため一部利益確定の参考警戒です。", 0.68m);
        }

        if (rate <= -20m && score.TotalScore < 40m)
        {
            return new DecisionResult(BotDecision.StopLoss, SellReasonType.StopLoss, "損失率が大きく、総合スコアも弱いため損失管理の参考警戒です。", 0.72m);
        }

        if (rate <= -10m && score.TotalScore < 45m)
        {
            return new DecisionResult(BotDecision.PartialStopLoss, SellReasonType.StopLoss, "損失が一定水準にあり、総合スコアが弱いため注意が必要です。", 0.68m);
        }

        if (score.TotalScore >= 75m && (rate is null || rate < 40m))
        {
            return new DecisionResult(BotDecision.BuyMore, SellReasonType.None, "総合スコアが高く、保有継続価値を強めに評価します。", 0.66m);
        }

        return new DecisionResult(BotDecision.Hold, SellReasonType.None, "総合スコアは中立圏で、保有継続価値を参考として扱います。", 0.6m);
    }
}
