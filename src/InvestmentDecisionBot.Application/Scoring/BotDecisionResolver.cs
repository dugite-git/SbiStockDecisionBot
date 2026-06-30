using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Application.DTOs;
using InvestmentDecisionBot.Domain.Enums;

namespace InvestmentDecisionBot.Application.Scoring;

public sealed class BotDecisionResolver : IBotDecisionResolver
{
    public DecisionResult Resolve(AnalysisInput input, ScoreResult score)
    {
        var risk = IsRiskAlert(score) ? " リスク警告条件に該当しています。" : "";

        if (input.TargetType == TargetType.Watchlist)
        {
            if (score.TotalScore >= 70m &&
                score.MomentumScore >= 55m &&
                score.PositionRiskScore >= 50m &&
                score.NewsScore >= 40m)
            {
                return new DecisionResult(BotDecision.NewBuy, SellReasonType.None, $"買い候補: 総合スコアと主要サブスコアが基準を上回っています。{risk}", 0.70m);
            }

            if (score.TotalScore >= 70m && score.NewsScore < 40m)
            {
                return new DecisionResult(BotDecision.Skip, SellReasonType.None, $"監視継続: 総合スコアは高い一方で、ニューススコアが弱めです。{risk}", 0.65m);
            }

            if (score.TotalScore >= 55m)
            {
                return new DecisionResult(BotDecision.Skip, SellReasonType.None, $"監視継続: スコアは中位で、継続確認の対象です。{risk}", 0.65m);
            }

            return new DecisionResult(BotDecision.Skip, SellReasonType.None, $"見送り: ウォッチリストの買い候補基準には届いていません。{risk}", 0.65m);
        }

        var rate = score.UnrealizedProfitLossRate;
        if (rate >= 20m && score.MomentumScore < 50m)
        {
            return new DecisionResult(BotDecision.TakeProfit, SellReasonType.TakeProfit, $"利確候補: 含み益が20%以上あり、モメンタムが弱まっています。{risk}", 0.72m);
        }

        if (rate >= 50m && score.NewsScore < 50m)
        {
            return new DecisionResult(BotDecision.TakeProfit, SellReasonType.TakeProfit, $"利確候補: 含み益が50%以上あり、ニューススコアが中立未満です。{risk}", 0.72m);
        }

        if (rate <= -10m && score.TotalScore < 50m)
        {
            return new DecisionResult(BotDecision.StopLoss, SellReasonType.StopLoss, $"損切り候補: 含み損が10%以上で、総合スコアも弱い状態です。{risk}", 0.72m);
        }

        if (rate <= -15m && score.MomentumScore < 45m)
        {
            return new DecisionResult(BotDecision.StopLoss, SellReasonType.StopLoss, $"損切り候補: 含み損が15%以上で、モメンタムも弱い状態です。{risk}", 0.72m);
        }

        if (score.TotalScore >= 55m && score.PositionRiskScore >= 40m)
        {
            return new DecisionResult(BotDecision.Hold, SellReasonType.None, $"保有継続: 総合スコアとポジションリスクは許容範囲です。{risk}", 0.65m);
        }

        return new DecisionResult(BotDecision.Hold, SellReasonType.None, $"注意付き保有: 売却条件には該当しませんが、望ましい保有基準は下回っています。{risk}", 0.58m);
    }

    private static bool IsRiskAlert(ScoreResult score) =>
        score.NewsScore <= 25m ||
        score.PositionRiskScore <= 25m ||
        (score.PositionRisk?.RawScore <= 25m);
}
