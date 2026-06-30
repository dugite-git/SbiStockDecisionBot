using InvestmentDecisionBot.Application.Reporting;
using InvestmentDecisionBot.Application.DTOs;
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
        Assert.Contains("SubScores: F", report.Content);
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
    public async Task DoesNotReportNewsAsMissingWhenSuccessfulNewsFetchReturnedNoArticles()
    {
        using var db = new TestDb();
        var security = new Security { Symbol = "2802", Name = "Ajinomoto", SecurityType = SecurityType.Stock, Country = "JP", Currency = "JPY" };
        db.Context.Securities.Add(security);
        db.Context.Holdings.Add(new Holding
        {
            Security = security,
            Quantity = 100,
            AverageAcquisitionPrice = 100m,
            AcquisitionAmount = 10000m,
            ImportedCurrentPrice = 110m,
            ImportedMarketValue = 11000m,
            ImportedUnrealizedProfitLoss = 1000m,
            IsActive = true
        });
        db.Context.ExternalApiCacheEntries.Add(new ExternalApiCacheEntry
        {
            Provider = "Gdelt",
            Function = "ArticleList",
            CacheKey = "2802",
            PayloadJson = """{"articles":[]}""",
            FetchedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(12),
            Succeeded = true
        });
        await db.Context.SaveChangesAsync();

        var dailyPrices = Enumerable.Range(0, 61)
            .Select(offset => new DailyPriceBar(DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-60 + offset)), 100m, 111m, 99m, 105m + offset * 0.1m, 1_000_000))
            .ToList();
        var financial = new FinancialSnapshotData(DateOnly.FromDateTime(DateTime.UtcNow.Date), 1000m, 120m, 110m, 80m, 50m, 500m, 2000m, 900m, 0.45m);
        var marketData = new FakeMarketDataProvider(110m) { DailyPrices = dailyPrices };
        var service = new ReportService(db.Context, new ScoreCalculator(), new BotDecisionResolver(), marketData, marketData, new FakeFinancialDataProvider(financial), new FakeDiscordPublisher(true), new NoopSystemLogService());

        await service.GenerateDailyReportAsync(postToDiscord: false, CancellationToken.None);

        var analysis = await db.Context.AnalysisResults.SingleAsync();
        Assert.DoesNotContain("news", analysis.MissingData, StringComparison.OrdinalIgnoreCase);
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
