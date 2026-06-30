using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Application.DTOs;
using InvestmentDecisionBot.Domain.Entities;

namespace InvestmentDecisionBot.Tests;

internal sealed class NoopSystemLogService : ISystemLogService
{
    public List<string> Messages { get; } = [];

    public Task LogAsync(string level, string category, string message, Exception? exception, CancellationToken cancellationToken)
    {
        Messages.Add(message);
        return Task.CompletedTask;
    }
}

internal sealed class FakeDiscordPublisher(bool succeeds) : IDiscordReportPublisher
{
    public Task<DiscordPostResult> PostReportAsync(string content, CancellationToken cancellationToken) =>
        Task.FromResult(succeeds
            ? new DiscordPostResult(true, "123", null)
            : new DiscordPostResult(false, null, "post failed"));
}

internal sealed class FakeAiAnalysisClient(bool succeeds) : IAiAnalysisClient
{
    public Task<AiAnalysisResultDto> AnalyzeAsync(AiAnalysisRequestDto request, CancellationToken cancellationToken) =>
        Task.FromResult(succeeds
            ? new AiAnalysisResultDto(true, request.BotDecision.Decision, request.BotDecision.SellReasonType, 0.7m, "補足情報です。", [], [], "{\"ok\":true}", null)
            : new AiAnalysisResultDto(false, null, null, 0m, "", [], [], null, "disabled"));
}

internal sealed class FakeMarketDataProvider(decimal? price = null, bool usedFallback = false) : IMarketDataProvider, ICachedMarketDataProvider
{
    public int CallCount { get; private set; }
    public IReadOnlyList<DailyPriceBar> DailyPrices { get; init; } = Array.Empty<DailyPriceBar>();
    public IReadOnlyList<NewsSentimentData> News { get; init; } = Array.Empty<NewsSentimentData>();

    public Task<MarketPriceResult> GetLatestPriceAsync(Security security, CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(new MarketPriceResult(price, security.Currency, usedFallback || price is null, price is null, price is null ? "missing" : null));
    }

    public Task<IReadOnlyList<DailyPriceBar>> GetCachedDailyPricesAsync(Security security, CancellationToken cancellationToken) =>
        Task.FromResult(DailyPrices);

    public Task<IReadOnlyList<NewsSentimentData>> GetCachedNewsAsync(Security security, CancellationToken cancellationToken) =>
        Task.FromResult(News);

    public Task<decimal?> GetCachedExchangeRateAsync(string fromCurrency, string toCurrency, CancellationToken cancellationToken) =>
        Task.FromResult<decimal?>(null);
}
