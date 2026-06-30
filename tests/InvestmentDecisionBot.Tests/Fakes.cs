using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Application.DTOs;
using InvestmentDecisionBot.Domain.Entities;
using System.Net;

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

internal sealed class FakeFinancialDataProvider(FinancialSnapshotData? snapshot = null) : IFinancialDataProvider
{
    public Task<FinancialDataResult> GetFinancialDataAsync(Security security, CancellationToken cancellationToken) =>
        Task.FromResult(snapshot is null ? new FinancialDataResult(false) : new FinancialDataResult(true, snapshot));
}

internal sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
{
    public int CallCount { get; private set; }
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        Requests.Add(request);
        return Task.FromResult(send(request));
    }

    public static HttpResponseMessage Json(string payload, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode) { Content = new StringContent(payload) };
}
