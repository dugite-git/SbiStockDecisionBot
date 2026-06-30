using InvestmentDecisionBot.Application.DTOs;
using InvestmentDecisionBot.Domain.Entities;
using InvestmentDecisionBot.Domain.Enums;
using InvestmentDecisionBot.Infrastructure.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace InvestmentDecisionBot.Tests.Providers;

public sealed class JQuantsMarketDataProviderTests
{
    [Fact]
    public async Task PrefetchSkipsJQuantsWhenApiKeyIsMissingAndFiltersNonJapaneseTargets()
    {
        using var db = new TestDb();
        var jp = new Security { Symbol = "7203", Name = "Toyota", SecurityType = SecurityType.Stock, Country = "JP", Currency = "JPY" };
        var us = new Security { Symbol = "NVDA", Name = "NVIDIA", SecurityType = SecurityType.Stock, Country = "US", Currency = "USD" };
        db.Context.Securities.AddRange(jp, us);
        db.Context.Holdings.AddRange(
            new Holding { Security = jp, Quantity = 1, AverageAcquisitionPrice = 1, AcquisitionAmount = 1, IsActive = true },
            new Holding { Security = us, Quantity = 1, AverageAcquisitionPrice = 1, AcquisitionAmount = 1, IsActive = true });
        await db.Context.SaveChangesAsync();

        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("""{"articles":[]}"""));
        var provider = CreateProvider(db, handler, new Dictionary<string, string?>
        {
            ["JQUANTS_API_KEY"] = "",
            ["EDINET_API_KEY"] = "",
            ["GDELT_MAX_RECORDS_PER_SECURITY"] = "1"
        });

        var result = await provider.PrefetchAsync(10, CancellationToken.None);

        Assert.Contains(result.Messages, message => message.Contains("JQUANTS_API_KEY"));
        Assert.Equal(1, result.Skipped);
        Assert.Equal(1, handler.CallCount);
        Assert.All(handler.Requests, request =>
        {
            var query = Uri.UnescapeDataString(request.RequestUri!.Query);
            Assert.Contains("query=(\"Toyota\" OR \"7203\")", query);
        });
    }

    [Fact]
    public async Task ReadsDailyPricesFinancialSnapshotAndNewsFromCache()
    {
        using var db = new TestDb();
        var security = new Security { Symbol = "7203", Name = "Toyota", SecurityType = SecurityType.Stock, Country = "JP", Currency = "JPY" };
        db.Context.Securities.Add(security);
        await db.Context.SaveChangesAsync();
        db.Context.ExternalApiCacheEntries.AddRange(
            Cache("JQuants", "DailyQuotes", "7203", """
            {"data":[{"Date":"2026-01-05","O":100,"H":110,"L":90,"C":105,"Vo":1000}]}
            """),
            Cache("JQuants", "Statements", "7203", """
            {"data":[{"DiscDate":"2026-01-10","Sales":1000,"OP":120,"NP":80,"EPS":50,"TA":2000,"Eq":900,"EqAR":0.45}]}
            """));
        db.Context.NewsItems.Add(new NewsItem
        {
            SecurityId = security.Id,
            Title = "Toyota profit upgrade",
            Url = "https://example.test/news",
            Source = "Gdelt",
            Sentiment = "Positive",
            FetchedAt = DateTimeOffset.UtcNow
        });
        await db.Context.SaveChangesAsync();

        var provider = CreateProvider(db, new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("{}")), []);

        var bars = await provider.GetCachedDailyPricesAsync(security, CancellationToken.None);
        var financial = await provider.GetFinancialDataAsync(security, CancellationToken.None);
        var news = await provider.GetCachedNewsAsync(security, CancellationToken.None);

        Assert.Single(bars);
        Assert.Equal(105m, bars[0].Close);
        Assert.True(financial.HasData);
        Assert.Equal(1000m, financial.Snapshot!.NetSales);
        Assert.Single(news);
        Assert.True(news[0].SentimentScore > 0);
    }

    [Fact]
    public async Task PrefetchUsesCurrentJQuantsV2Endpoints()
    {
        using var db = new TestDb();
        var security = new Security { Symbol = "7203", Name = "Toyota", SecurityType = SecurityType.Stock, Country = "JP", Currency = "JPY" };
        db.Context.Securities.Add(security);
        db.Context.WatchlistItems.Add(new WatchlistItem { Security = security, IsActive = true });
        await db.Context.SaveChangesAsync();

        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("""{"data":[]}"""));
        var provider = CreateProvider(db, handler, new Dictionary<string, string?>
        {
            ["JQUANTS_API_KEY"] = "test-key",
            ["EDINET_API_KEY"] = "",
            ["JQUANTS_RATE_LIMIT_PER_MINUTE"] = "1000000",
            ["GDELT_MAX_RECORDS_PER_SECURITY"] = "1"
        });

        await provider.PrefetchAsync(1, CancellationToken.None);

        var jQuantsPaths = handler.Requests
            .Select(request => request.RequestUri!)
            .Where(uri => uri.Host == "api.jquants.com")
            .Select(uri => uri.AbsolutePath)
            .ToList();
        Assert.Contains("/v2/equities/master", jQuantsPaths);
        Assert.Contains("/v2/equities/bars/daily", jQuantsPaths);
        Assert.Contains("/v2/fins/summary", jQuantsPaths);
        Assert.Contains("/v2/equities/earnings-calendar", jQuantsPaths);
    }

    [Fact]
    public async Task PrefetchReportsNonJsonApiPayloadWithoutThrowing()
    {
        using var db = new TestDb();
        var security = new Security { Symbol = "7203", Name = "Toyota", SecurityType = SecurityType.Stock, Country = "JP", Currency = "JPY" };
        db.Context.Securities.Add(security);
        db.Context.WatchlistItems.Add(new WatchlistItem { Security = security, IsActive = true });
        await db.Context.SaveChangesAsync();

        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("Quota exceeded"));
        var provider = CreateProvider(db, handler, new Dictionary<string, string?>
        {
            ["JQUANTS_API_KEY"] = "",
            ["EDINET_API_KEY"] = "",
            ["GDELT_MAX_RECORDS_PER_SECURITY"] = "1"
        });

        var result = await provider.PrefetchAsync(1, CancellationToken.None);

        Assert.Contains(result.RequestLogs, log => !log.Succeeded && log.ErrorMessage?.Contains("non-JSON payload") == true);
    }

    [Fact]
    public async Task PrefetchInvalidatesNonJsonCacheAndRefetches()
    {
        using var db = new TestDb();
        var security = new Security { Symbol = "7203", Name = "Toyota", SecurityType = SecurityType.Stock, Country = "JP", Currency = "JPY" };
        db.Context.Securities.Add(security);
        db.Context.WatchlistItems.Add(new WatchlistItem { Security = security, IsActive = true });
        db.Context.ExternalApiCacheEntries.Add(Cache("Gdelt", "ArticleList", "7203", "Quota exceeded"));
        await db.Context.SaveChangesAsync();

        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("""{"articles":[]}"""));
        var provider = CreateProvider(db, handler, new Dictionary<string, string?>
        {
            ["JQUANTS_API_KEY"] = "",
            ["EDINET_API_KEY"] = "",
            ["GDELT_MAX_RECORDS_PER_SECURITY"] = "1"
        });

        var result = await provider.PrefetchAsync(1, CancellationToken.None);

        Assert.Contains(result.RequestLogs, log => log.Function == "ArticleList" && log.Succeeded);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task PrefetchGdeltStoresArticlesAsNewsItems()
    {
        using var db = new TestDb();
        var security = new Security { Symbol = "7203", Name = "Toyota", SecurityType = SecurityType.Stock, Country = "JP", Currency = "JPY" };
        db.Context.Securities.Add(security);
        db.Context.WatchlistItems.Add(new WatchlistItem { Security = security, IsActive = true });
        await db.Context.SaveChangesAsync();

        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("""
        {"articles":[{"url":"https://example.test/toyota","title":"Toyota profit upgrade","seendate":"20260630T010203Z","domain":"example.test"}]}
        """));
        var provider = CreateProvider(db, handler, new Dictionary<string, string?>
        {
            ["JQUANTS_API_KEY"] = "",
            ["EDINET_API_KEY"] = "",
            ["GDELT_MAX_RECORDS_PER_SECURITY"] = "1"
        });

        var result = await provider.PrefetchAsync(1, CancellationToken.None);

        Assert.Contains(result.RequestLogs, log => log.Function == "ArticleList" && log.Succeeded);
        var news = Assert.Single(db.Context.NewsItems);
        Assert.Equal(security.Id, news.SecurityId);
        Assert.Equal("Toyota profit upgrade", news.Title);
        Assert.Equal("Positive", news.Sentiment);
    }

    private static JQuantsMarketDataProvider CreateProvider(TestDb db, HttpMessageHandler handler, IEnumerable<KeyValuePair<string, string?>> settings)
    {
        var defaults = new Dictionary<string, string?> { ["GDELT_REQUEST_DELAY_MS"] = "0" };
        foreach (var setting in settings)
        {
            defaults[setting.Key] = setting.Value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(defaults).Build();
        return new JQuantsMarketDataProvider(db.Context, new FakeHttpClientFactory(handler), configuration, NullLogger<JQuantsMarketDataProvider>.Instance);
    }

    private static ExternalApiCacheEntry Cache(string provider, string function, string key, string payload) =>
        new()
        {
            Provider = provider,
            Function = function,
            CacheKey = key,
            PayloadJson = payload,
            FetchedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            Succeeded = true
        };
}
