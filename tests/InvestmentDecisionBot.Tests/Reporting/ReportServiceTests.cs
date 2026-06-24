using InvestmentDecisionBot.Application.Reporting;
using InvestmentDecisionBot.Application.Scoring;
using InvestmentDecisionBot.Domain.Entities;
using InvestmentDecisionBot.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace InvestmentDecisionBot.Tests.Reporting;

public sealed class ReportServiceTests
{
    [Fact]
    public async Task GeneratesReportWithWarningsAndSavesDailyReport()
    {
        using var db = new TestDb();
        var security = new Security { Symbol = "7203", Name = "Toyota", SecurityType = SecurityType.Stock };
        db.Context.Securities.Add(security);
        db.Context.Holdings.Add(new Holding
        {
            Security = security,
            Quantity = 100,
            AverageAcquisitionPrice = 2000,
            AcquisitionAmount = 200000,
            ImportedCurrentPrice = 2500,
            ImportedMarketValue = 250000,
            ImportedUnrealizedProfitLoss = 50000,
            IsActive = true
        });
        await db.Context.SaveChangesAsync();

        var service = new ReportService(db.Context, new ScoreCalculator(), new BotDecisionResolver(), new FakeAiAnalysisClient(false), new FakeDiscordPublisher(false), new NoopSystemLogService());
        var report = await service.GenerateDailyReportAsync(postToDiscord: true, CancellationToken.None);

        Assert.False(report.Succeeded);
        Assert.Contains("投資判断の参考情報", report.Content);
        Assert.Contains("AI分析: 未実行または失敗", report.Content);
        Assert.DoesNotContain("買うべき", report.Content);
        Assert.Single(await db.Context.DailyReports.ToListAsync());
        Assert.False((await db.Context.DailyReports.SingleAsync()).PostedToDiscord);
    }
}
