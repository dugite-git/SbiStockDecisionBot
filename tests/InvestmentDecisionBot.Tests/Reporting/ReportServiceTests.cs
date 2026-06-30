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
        var security = new Security { Symbol = "7203", Name = "Toyota", SecurityType = SecurityType.Stock, Country = "JP", Currency = "JPY" };
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

        var marketData = new FakeMarketDataProvider();
        var service = new ReportService(db.Context, new ScoreCalculator(), new BotDecisionResolver(), marketData, marketData, new FakeFinancialDataProvider(), new FakeDiscordPublisher(false), new NoopSystemLogService());
        var report = await service.GenerateDailyReportAsync(postToDiscord: true, CancellationToken.None);

        Assert.False(report.Succeeded);
        Assert.Contains("7203", report.Content);
        Assert.DoesNotContain("AI", report.Content);
        Assert.Empty(await db.Context.AiAnalysisLogs.ToListAsync());
        Assert.Single(await db.Context.DailyReports.ToListAsync());
        Assert.False((await db.Context.DailyReports.SingleAsync()).PostedToDiscord);
    }

    [Fact]
    public async Task PrefersImportedSbiPriceForHoldingAnalysisAndSavesProviderSnapshot()
    {
        using var db = new TestDb();
        var security = new Security { Symbol = "7203", Name = "Toyota", SecurityType = SecurityType.Stock, Country = "JP", Currency = "JPY" };
        db.Context.Securities.Add(security);
        db.Context.Holdings.Add(new Holding
        {
            Security = security,
            Quantity = 100,
            AverageAcquisitionPrice = 2000,
            AcquisitionAmount = 200000,
            ImportedCurrentPrice = 2100,
            ImportedMarketValue = 210000,
            ImportedUnrealizedProfitLoss = 10000,
            IsActive = true
        });
        await db.Context.SaveChangesAsync();

        var marketData = new FakeMarketDataProvider(3000m);
        var service = new ReportService(db.Context, new ScoreCalculator(), new BotDecisionResolver(), marketData, marketData, new FakeFinancialDataProvider(), new FakeDiscordPublisher(true), new NoopSystemLogService());
        var report = await service.GenerateDailyReportAsync(postToDiscord: false, CancellationToken.None);

        Assert.True(report.Succeeded);
        var analysis = await db.Context.AnalysisResults.SingleAsync();
        Assert.Equal(50.05m, analysis.TotalScore);
        Assert.Equal(BotDecision.Hold, analysis.BotDecision);

        var snapshot = await db.Context.MarketPriceSnapshots.SingleAsync();
        Assert.Equal(3000m, snapshot.Price);
        Assert.Equal("JPY", snapshot.Currency);
        Assert.False(snapshot.UsedFallback);
    }

    [Fact]
    public async Task ReusesSameDayMarketPriceSnapshotWithoutCallingProvider()
    {
        using var db = new TestDb();
        var security = new Security { Symbol = "7203", Name = "Toyota", SecurityType = SecurityType.Stock, Country = "JP", Currency = "JPY" };
        db.Context.Securities.Add(security);
        db.Context.Holdings.Add(new Holding
        {
            Security = security,
            Quantity = 100,
            AverageAcquisitionPrice = 2000,
            AcquisitionAmount = 200000,
            ImportedCurrentPrice = 2100,
            ImportedMarketValue = 210000,
            ImportedUnrealizedProfitLoss = 10000,
            IsActive = true
        });
        db.Context.MarketPriceSnapshots.Add(new MarketPriceSnapshot
        {
            Security = security,
            Price = 3000m,
            Currency = "JPY",
            FetchedAt = DateTimeOffset.UtcNow,
            DataSource = "test",
            IsStale = false,
            UsedFallback = false
        });
        await db.Context.SaveChangesAsync();

        var marketData = new FakeMarketDataProvider(9999m);
        var service = new ReportService(db.Context, new ScoreCalculator(), new BotDecisionResolver(), marketData, marketData, new FakeFinancialDataProvider(), new FakeDiscordPublisher(true), new NoopSystemLogService());
        var report = await service.GenerateDailyReportAsync(postToDiscord: false, CancellationToken.None);

        Assert.True(report.Succeeded);
        Assert.Equal(0, marketData.CallCount);
        Assert.Single(await db.Context.MarketPriceSnapshots.ToListAsync());

        var analysis = await db.Context.AnalysisResults.SingleAsync();
        Assert.Equal(50.05m, analysis.TotalScore);
        Assert.Equal(BotDecision.Hold, analysis.BotDecision);
    }

    [Fact]
    public async Task DoesNotReuseSameDayEmptyMarketPriceSnapshot()
    {
        using var db = new TestDb();
        var security = new Security { Symbol = "7203", Name = "Toyota", SecurityType = SecurityType.Stock, Country = "JP", Currency = "JPY" };
        db.Context.Securities.Add(security);
        db.Context.Holdings.Add(new Holding
        {
            Security = security,
            Quantity = 100,
            AverageAcquisitionPrice = 2000,
            AcquisitionAmount = 200000,
            ImportedCurrentPrice = 2100,
            ImportedMarketValue = 210000,
            ImportedUnrealizedProfitLoss = 10000,
            IsActive = true
        });
        db.Context.MarketPriceSnapshots.Add(new MarketPriceSnapshot
        {
            Security = security,
            Price = null,
            Currency = "JPY",
            FetchedAt = DateTimeOffset.UtcNow,
            DataSource = "test",
            IsStale = true,
            UsedFallback = true,
            ErrorMessage = "empty"
        });
        await db.Context.SaveChangesAsync();

        var marketData = new FakeMarketDataProvider(3000m);
        var service = new ReportService(db.Context, new ScoreCalculator(), new BotDecisionResolver(), marketData, marketData, new FakeFinancialDataProvider(), new FakeDiscordPublisher(true), new NoopSystemLogService());
        var report = await service.GenerateDailyReportAsync(postToDiscord: false, CancellationToken.None);

        Assert.True(report.Succeeded);
        Assert.Equal(1, marketData.CallCount);
        Assert.Equal(2, await db.Context.MarketPriceSnapshots.CountAsync());

        var latestSnapshot = (await db.Context.MarketPriceSnapshots.ToListAsync()).OrderByDescending(snapshot => snapshot.FetchedAt).First();
        Assert.Equal(3000m, latestSnapshot.Price);
        Assert.False(latestSnapshot.UsedFallback);
    }

    [Fact]
    public async Task SkipsExistingUsStocksInReports()
    {
        using var db = new TestDb();
        var usSecurity = new Security { Symbol = "NVDA", Name = "NVIDIA", SecurityType = SecurityType.Stock, Country = "US", Currency = "USD" };
        db.Context.Securities.Add(usSecurity);
        db.Context.Holdings.Add(new Holding
        {
            Security = usSecurity,
            Quantity = 1,
            AverageAcquisitionPrice = 100,
            AcquisitionAmount = 100,
            ImportedCurrentPrice = 120,
            ImportedMarketValue = 120,
            ImportedUnrealizedProfitLoss = 20,
            IsActive = true
        });
        await db.Context.SaveChangesAsync();

        var marketData = new FakeMarketDataProvider(3000m);
        var service = new ReportService(db.Context, new ScoreCalculator(), new BotDecisionResolver(), marketData, marketData, new FakeFinancialDataProvider(), new FakeDiscordPublisher(true), new NoopSystemLogService());
        var report = await service.GenerateDailyReportAsync(postToDiscord: false, CancellationToken.None);

        Assert.True(report.Succeeded);
        Assert.Equal(0, report.AnalysisCount);
        Assert.Empty(await db.Context.AnalysisResults.ToListAsync());
        Assert.Equal(0, marketData.CallCount);
    }
}
