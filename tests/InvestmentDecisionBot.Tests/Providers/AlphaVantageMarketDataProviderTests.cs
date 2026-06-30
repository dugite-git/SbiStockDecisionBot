using System.Net;
using InvestmentDecisionBot.Domain.Entities;
using InvestmentDecisionBot.Infrastructure.Providers;
using InvestmentDecisionBot.Tests;
using Microsoft.Extensions.Options;

namespace InvestmentDecisionBot.Tests.Providers;

public sealed class AlphaVantageMarketDataProviderTests
{
    [Fact]
    public async Task UsesFreshApiCacheWithoutCallingHttp()
    {
        using var db = new TestDb();
        var security = new Security { Symbol = "IBM", Currency = "USD" };
        db.Context.Securities.Add(security);
        db.Context.ExternalApiCacheEntries.Add(
            new ExternalApiCacheEntry
            {
                Provider = "AlphaVantage",
                Function = "GLOBAL_QUOTE",
                CacheKey = "IBM",
                PayloadJson = """{"Global Quote":{"05. price":"278.0000"}}""",
                FetchedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                Succeeded = true
            });
        await db.Context.SaveChangesAsync();
        var handler = new QueueHttpMessageHandler();
        var provider = CreateProvider(handler, db);

        var result = await provider.GetLatestPriceAsync(security, CancellationToken.None);

        Assert.Equal(0, handler.CallCount);
        Assert.Equal(278m, result.Price);
        Assert.Equal("IBM", security.AlphaVantageSymbol);
    }

    [Fact]
    public async Task DoesNotCallHttpWhenDailyLimitIsReached()
    {
        using var db = new TestDb();
        var security = new Security { Symbol = "IBM", Currency = "USD" };
        db.Context.Securities.Add(security);
        for (var i = 0; i < 25; i++)
        {
            db.Context.ExternalApiRequestLogs.Add(new ExternalApiRequestLog
            {
                Provider = "AlphaVantage",
                Function = "GLOBAL_QUOTE",
                CacheKey = i.ToString(),
                RequestedAt = DateTimeOffset.UtcNow,
                Succeeded = true
            });
        }

        await db.Context.SaveChangesAsync();
        var handler = new QueueHttpMessageHandler("""{"bestMatches":[]}""");
        var provider = CreateProvider(handler, db);

        var result = await provider.GetLatestPriceAsync(security, CancellationToken.None);

        Assert.Equal(0, handler.CallCount);
        Assert.Null(result.Price);
        Assert.True(result.UsedFallback);
    }

    [Fact]
    public async Task PrefetchStopsAtRemainingDailyBudget()
    {
        using var db = new TestDb();
        db.Context.Securities.AddRange(
            new Security { Symbol = "IBM", Currency = "USD", AlphaVantageSymbol = "IBM" },
            new Security { Symbol = "MSFT", Currency = "USD", AlphaVantageSymbol = "MSFT" });
        await db.Context.SaveChangesAsync();
        db.Context.WatchlistItems.AddRange(db.Context.Securities.Select(security => new WatchlistItem { SecurityId = security.Id, IsActive = true }));
        for (var i = 0; i < 24; i++)
        {
            db.Context.ExternalApiRequestLogs.Add(new ExternalApiRequestLog
            {
                Provider = "AlphaVantage",
                Function = "GLOBAL_QUOTE",
                CacheKey = i.ToString(),
                RequestedAt = DateTimeOffset.UtcNow,
                Succeeded = true
            });
        }

        await db.Context.SaveChangesAsync();
        var handler = new QueueHttpMessageHandler("""{"bestMatches":[{"1. symbol":"IBM","3. type":"Equity","4. region":"United States","8. currency":"USD","9. matchScore":"1.0000"}]}""");
        var provider = CreateProvider(handler, db);

        var result = await provider.PrefetchAsync(5, CancellationToken.None);

        Assert.Equal(1, result.Attempted);
        Assert.Equal(25, result.UsedToday);
        Assert.Equal(0, result.RemainingToday);
    }

    [Fact]
    public async Task CoverageReportsResolvedSymbolsAndCachedData()
    {
        using var db = new TestDb();
        var ibm = new Security { Symbol = "IBM", Name = "IBM", Currency = "USD", AlphaVantageSymbol = "IBM" };
        var toyota = new Security { Symbol = "7203", Name = "Toyota", Currency = "JPY", AlphaVantageSymbolResolutionError = "not resolved" };
        db.Context.Securities.AddRange(ibm, toyota);
        await db.Context.SaveChangesAsync();
        db.Context.Holdings.Add(new Holding
        {
            SecurityId = ibm.Id,
            Quantity = 1,
            AverageAcquisitionPrice = 100,
            AcquisitionAmount = 100,
            IsActive = true
        });
        db.Context.WatchlistItems.Add(new WatchlistItem { SecurityId = toyota.Id, IsActive = true });
        db.Context.MarketPriceSnapshots.Add(new MarketPriceSnapshot
        {
            SecurityId = ibm.Id,
            Price = 120m,
            Currency = "USD",
            FetchedAt = DateTimeOffset.UtcNow,
            DataSource = "test",
            UsedFallback = false,
            IsStale = false
        });
        db.Context.ExternalApiCacheEntries.AddRange(
            new ExternalApiCacheEntry
            {
                Provider = "AlphaVantage",
                Function = "TIME_SERIES_DAILY",
                CacheKey = "IBM",
                PayloadJson = "{}",
                FetchedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                Succeeded = true
            },
            new ExternalApiCacheEntry
            {
                Provider = "AlphaVantage",
                Function = "NEWS_SENTIMENT",
                CacheKey = "IBM",
                PayloadJson = "{}",
                FetchedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                Succeeded = true
            },
            new ExternalApiCacheEntry
            {
                Provider = "AlphaVantage",
                Function = "CURRENCY_EXCHANGE_RATE",
                CacheKey = "USD_JPY",
                PayloadJson = "{}",
                FetchedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                Succeeded = true
            });
        await db.Context.SaveChangesAsync();
        var provider = CreateProvider(new QueueHttpMessageHandler(), db);

        var coverage = await provider.GetCoverageAsync(CancellationToken.None);

        Assert.Equal(2, coverage.TargetCount);
        Assert.Equal(1, coverage.AlphaVantageCoveredCount);
        Assert.Equal(1, coverage.PriceCachedCount);
        Assert.Equal(1, coverage.DailyCachedCount);
        Assert.Equal(1, coverage.NewsCachedCount);
        Assert.Equal(2, coverage.ExchangeRateCachedCount);
        Assert.Contains(coverage.Items, item => item.Symbol == "7203" && item.ResolutionError == "not resolved");
    }

    [Fact]
    public async Task DoesNotRetryWhenGlobalQuoteIsEmpty()
    {
        using var db = new TestDb();
        var security = new Security { Symbol = "IBM", Currency = "USD" };
        db.Context.Securities.Add(security);
        await db.Context.SaveChangesAsync();
        var handler = new QueueHttpMessageHandler("""{"Global Quote":{}}""");
        var provider = CreateProvider(handler, db);

        var result = await provider.GetLatestPriceAsync(security, CancellationToken.None);

        Assert.Equal(1, handler.CallCount);
        Assert.Null(result.Price);
        Assert.True(result.UsedFallback);
    }

    [Fact]
    public async Task ResolvesJapaneseAndUsSymbolsWithoutSymbolSearchRequests()
    {
        using var db = new TestDb();
        var toyota = new Security { Symbol = "7203", Country = "JP", Currency = "JPY" };
        var ibm = new Security { Symbol = "IBM", Country = "US", Currency = "USD" };
        db.Context.Securities.AddRange(toyota, ibm);
        await db.Context.SaveChangesAsync();
        var handler = new QueueHttpMessageHandler(
            """{"Global Quote":{"05. price":"3000.0000"}}""",
            """{"Global Quote":{"05. price":"278.0000"}}""");
        var provider = CreateProvider(handler, db);

        await provider.GetLatestPriceAsync(toyota, CancellationToken.None);
        await provider.GetLatestPriceAsync(ibm, CancellationToken.None);

        Assert.DoesNotContain(handler.RequestUris, uri => uri.Query.Contains("function=SYMBOL_SEARCH", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("symbol=7203.T", handler.RequestUris[0].Query);
        Assert.Contains("symbol=IBM", handler.RequestUris[1].Query);
        Assert.Equal("7203.T", toyota.AlphaVantageSymbol);
        Assert.Equal("IBM", ibm.AlphaVantageSymbol);
    }

    [Fact]
    public async Task ResolvesJapaneseFourDigitSymbolsToTokyoSuffix()
    {
        using var db = new TestDb();
        var security = new Security { Symbol = "7701", Country = "JP", Currency = "JPY" };
        db.Context.Securities.Add(security);
        await db.Context.SaveChangesAsync();
        var handler = new QueueHttpMessageHandler("""{"Global Quote":{"05. price":"4200.0000"}}""");
        var provider = CreateProvider(handler, db);

        var result = await provider.GetLatestPriceAsync(security, CancellationToken.None);

        Assert.Equal(4200m, result.Price);
        Assert.Equal("7701.T", security.AlphaVantageSymbol);
        Assert.Null(security.AlphaVantageSymbolResolutionError);
        Assert.DoesNotContain(handler.RequestUris, uri => uri.Query.Contains("function=SYMBOL_SEARCH", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("symbol=7701.T", handler.RequestUris[0].Query);
    }

    [Fact]
    public async Task SpacesHttpRequestsByConfiguredInterval()
    {
        using var db = new TestDb();
        var first = new Security { Symbol = "IBM", Country = "US", Currency = "USD" };
        var second = new Security { Symbol = "MSFT", Country = "US", Currency = "USD" };
        db.Context.Securities.AddRange(first, second);
        await db.Context.SaveChangesAsync();
        var handler = new QueueHttpMessageHandler(
            """{"Global Quote":{"05. price":"278.0000"}}""",
            """{"Global Quote":{"05. price":"500.0000"}}""");
        var provider = CreateProvider(handler, db, minimumIntervalMs: 50);

        await provider.GetLatestPriceAsync(first, CancellationToken.None);
        await provider.GetLatestPriceAsync(second, CancellationToken.None);

        Assert.True(handler.RequestStartedAt.Zip(handler.RequestStartedAt.Skip(1), (a, b) => b - a).All(delta => delta >= TimeSpan.FromMilliseconds(40)));
    }

    private static AlphaVantageMarketDataProvider CreateProvider(QueueHttpMessageHandler handler, TestDb db, int minimumIntervalMs = 0) =>
        new(
            new HttpClient(handler),
            db.Context,
            Options.Create(new AlphaVantageOptions
            {
                Enabled = true,
                FetchOnReport = true,
                ApiKey = "test-key",
                MinimumRequestIntervalMilliseconds = minimumIntervalMs
            }));

    private sealed class QueueHttpMessageHandler(params string[] responses) : HttpMessageHandler
    {
        private readonly Queue<string> responses = new(responses);

        public int CallCount { get; private set; }
        public List<Uri> RequestUris { get; } = [];
        public List<DateTimeOffset> RequestStartedAt { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUris.Add(request.RequestUri!);
            RequestStartedAt.Add(DateTimeOffset.UtcNow);
            var content = responses.Count > 0 ? responses.Dequeue() : """{"Global Quote":{}}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }
    }
}
