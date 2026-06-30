using InvestmentDecisionBot.Application.DTOs;

namespace InvestmentDecisionBot.Application.Abstractions;

public interface IImportService
{
    Task<SbiImportResult> ImportSbiCsvAsync(Stream csvStream, string? fileName, CancellationToken cancellationToken);
}

public interface IWatchlistService
{
    Task<WatchlistMutationResult> AddAsync(string symbol, CancellationToken cancellationToken);
    Task<WatchlistMutationResult> RemoveAsync(string symbol, CancellationToken cancellationToken);
    Task<IReadOnlyList<WatchlistItemDto>> ListAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<WatchTargetDto>> ListTargetsAsync(CancellationToken cancellationToken);
}

public interface IReportService
{
    Task<ReportResult> GenerateDailyReportAsync(bool postToDiscord, CancellationToken cancellationToken);
}

public interface IMarketDataPrefetchService
{
    Task<MarketDataStatusResult> GetStatusAsync(CancellationToken cancellationToken);
    Task<MarketDataPrefetchResult> PrefetchAsync(int? limit, CancellationToken cancellationToken);
    Task<MarketDataCoverageResult> GetCoverageAsync(CancellationToken cancellationToken);
    Task<MarketDataDetailResult> GetDetailAsync(string symbol, CancellationToken cancellationToken);
}

public interface IScoreCalculator
{
    ScoreResult Calculate(AnalysisInput input);
}

public interface IBotDecisionResolver
{
    DecisionResult Resolve(AnalysisInput input, ScoreResult score);
}

public interface ISystemLogService
{
    Task LogAsync(string level, string category, string message, Exception? exception, CancellationToken cancellationToken);
}

public interface IDiscordReportPublisher
{
    Task<DiscordPostResult> PostReportAsync(string content, CancellationToken cancellationToken);
}
