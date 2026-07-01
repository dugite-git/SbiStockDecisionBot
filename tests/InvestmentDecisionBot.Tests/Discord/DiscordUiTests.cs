using InvestmentDecisionBot.Application.DTOs;
using InvestmentDecisionBot.Domain.Enums;
using InvestmentDecisionBot.Presentation.Discord.Ui;

namespace InvestmentDecisionBot.Tests.Discord;

public sealed class DiscordUiTests
{
    [Theory]
    [InlineData("7203", true, "7203")]
    [InlineData(" 7203 ", true, "7203")]
    [InlineData("７２０３", true, "7203")]
    [InlineData("7203.T", true, "7203")]
    [InlineData("AAPL", false, "AAPL")]
    public void NormalizesJapaneseStockSymbols(string input, bool expectedValid, string expectedSymbol)
    {
        var result = SymbolNormalizer.NormalizeJapaneseStockSymbol(input);

        Assert.Equal(expectedValid, result.IsValid);
        Assert.Equal(expectedSymbol, result.Symbol);
    }

    [Fact]
    public void TruncateKeepsTextWithinLimit()
    {
        var result = DiscordFormatters.Truncate(new string('a', 200), 50);

        Assert.True(result.Length <= 50);
        Assert.Contains("省略", result);
    }

    [Fact]
    public void PageLabelDisplaysOneBasedPageCount()
    {
        Assert.Equal("1/3", DiscordFormatters.PageLabel(0, 3));
        Assert.Equal("3/3", DiscordFormatters.PageLabel(2, 3));
    }

    [Theory]
    [InlineData(BotDecision.TakeProfit, "利確候補")]
    [InlineData(BotDecision.PartialTakeProfit, "一部利確候補")]
    [InlineData(BotDecision.StopLoss, "損切り候補")]
    [InlineData(BotDecision.PartialStopLoss, "一部損切り候補")]
    [InlineData(BotDecision.NewBuy, "新規買い候補")]
    [InlineData(BotDecision.BuyMore, "買い増し候補")]
    [InlineData(BotDecision.Hold, "保有継続")]
    [InlineData(BotDecision.Skip, "見送り")]
    [InlineData(BotDecision.AnalysisFailed, "分析失敗")]
    public void FormatDecisionLocalizesAllBotDecisions(BotDecision decision, string expected)
    {
        Assert.Contains(expected, DiscordFormatters.FormatDecision(decision));
    }

    [Fact]
    public void FormatJstReturnsJstText()
    {
        var formatted = DiscordFormatters.FormatJst(DateTimeOffset.UtcNow);

        Assert.Contains("JST", formatted);
    }

    [Fact]
    public void CoverageMissingDetectsAnyMissingData()
    {
        var missing = new MarketDataCoverageItem("7203", "Toyota", "Holding", "7203.T", true, true, true, false, true, null, null, null, null);
        var complete = new MarketDataCoverageItem("6758", "Sony", "Watchlist", "6758.T", true, true, true, true, true, null, null, null, null);

        Assert.True(DiscordFormatters.HasCoverageMissing(missing));
        Assert.False(DiscordFormatters.HasCoverageMissing(complete));
        Assert.Equal("✅", DiscordFormatters.CoverageIcon(true));
        Assert.Equal("❌", DiscordFormatters.CoverageIcon(false));
    }

    [Fact]
    public void ReportSummaryPrioritizesImportantItemsAndCounts()
    {
        var important = new ReportDecisionSummaryItem(
            "7203",
            "Toyota",
            TargetType.Holding,
            BotDecision.TakeProfit,
            72m,
            18.5m,
            0.8m,
            "利益確定圏です。",
            72m,
            65m,
            81m,
            50m,
            60m,
            [],
            []);
        var skipped = new ReportDecisionSummaryItem(
            "6758",
            "Sony",
            TargetType.Watchlist,
            BotDecision.Skip,
            50m,
            null,
            0.65m,
            "見送りです。",
            50m,
            50m,
            50m,
            50m,
            50m,
            [],
            []);
        var result = new ReportResult(
            true,
            "# report",
            2,
            null,
            null,
            1,
            null,
            null,
            null,
            [important],
            [important, skipped],
            new ReportDecisionCounts(1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 1),
            ["news"]);

        var summary = DiscordFormatters.BuildReportSummary(result);

        Assert.Contains("重要判断: 1件", summary);
        Assert.Contains("`7203` Toyota", summary);
        Assert.Contains("SubScores: F 72", summary);
        Assert.Contains("見送り", summary);
        Assert.Contains("監視継続", summary);
        Assert.Contains("不足データ: news", summary);
    }
}
