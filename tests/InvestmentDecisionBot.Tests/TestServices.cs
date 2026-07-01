using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Application.Reporting;
using InvestmentDecisionBot.Application.Scoring;
using InvestmentDecisionBot.Application.Watchlist;
using InvestmentDecisionBot.Infrastructure.Csv;
using InvestmentDecisionBot.Infrastructure.Persistence;
using InvestmentDecisionBot.Infrastructure.Persistence.Repositories;
using InvestmentDecisionBot.Application.Importing;

namespace InvestmentDecisionBot.Tests;

internal static class TestServices
{
    public static ImportService CreateImportService(BotDbContext db, ISystemLogService? logs = null)
    {
        return new ImportService(
            new SbiCsvParser(),
            new EfSecurityRepository(db),
            new EfHoldingRepository(db),
            new EfHoldingSnapshotRepository(db),
            new EfImportBatchRepository(db),
            new EfWatchlistRepository(db),
            new EfSoldEventRepository(db),
            new EfUnitOfWork(db),
            logs ?? new NoopSystemLogService());
    }

    public static WatchlistService CreateWatchlistService(BotDbContext db)
    {
        return new WatchlistService(
            new EfSecurityRepository(db),
            new EfHoldingRepository(db),
            new EfWatchlistRepository(db),
            new EfUnitOfWork(db));
    }

    public static ReportService CreateReportService(
        BotDbContext db,
        IMarketDataProvider marketData,
        ICachedMarketDataProvider cachedMarketData,
        IFinancialDataProvider? financialData = null,
        IDiscordReportPublisher? publisher = null,
        ISystemLogService? logs = null)
    {
        return new ReportService(
            new EfHoldingRepository(db),
            new EfWatchlistRepository(db),
            new EfMarketPriceSnapshotRepository(db),
            new EfExternalApiCacheRepository(db),
            new EfAnalysisResultRepository(db),
            new EfAnalysisRunRepository(db),
            new EfDailyReportRepository(db),
            new EfUnitOfWork(db),
            new ScoreCalculator(),
            new BotDecisionResolver(),
            marketData,
            cachedMarketData,
            financialData ?? new FakeFinancialDataProvider(),
            publisher ?? new FakeDiscordPublisher(true),
            logs ?? new NoopSystemLogService());
    }
}
