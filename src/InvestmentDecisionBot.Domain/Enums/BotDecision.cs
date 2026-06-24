namespace InvestmentDecisionBot.Domain.Enums;

public enum BotDecision
{
    BuyMore = 0,
    Hold = 1,
    PartialTakeProfit = 2,
    TakeProfit = 3,
    PartialStopLoss = 4,
    StopLoss = 5,
    AnalysisFailed = 6,
    NewBuy = 7,
    Skip = 8
}
