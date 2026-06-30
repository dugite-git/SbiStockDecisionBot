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
        Assert.All(handler.Requests, request => Assert.Contains("Toyota", request.RequestUri!.Query));
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
            {"daily_quotes":[{"Date":"2026-01-05","Open":100,"High":110,"Low":90,"Close":105,"Volume":1000}]}
            """),
            Cache("JQuants", "Statements", "7203", """
            {"statements":[{"DisclosedDate":"2026-01-10","NetSales":1000,"OperatingProfit":120,"Profit":80,"EarningsPerShare":50,"TotalAssets":2000,"NetAssets":900,"EquityToAssetRatio":0.45}]}
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

    private static JQuantsMarketDataProvider CreateProvider(TestDb db, HttpMessageHandler handler, IEnumerable<KeyValuePair<string, string?>> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
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
