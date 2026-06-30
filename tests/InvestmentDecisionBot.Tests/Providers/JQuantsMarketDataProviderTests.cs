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
            Assert.Contains("query=\"Toyota\"", query);
            Assert.DoesNotContain("\"7203\"", query);
        });
    }

    [Fact]
    public async Task PrefetchGdeltDoesNotRequestWhenOnlyShortOrNumericSearchTermsExist()
    {
        using var db = new TestDb();
        var shortName = new Security { Symbol = "9432", Name = "NTT", SecurityType = SecurityType.Stock, Country = "JP", Currency = "JPY" };
        var symbolName = new Security { Symbol = "7201", Name = "7201", SecurityType = SecurityType.Stock, Country = "JP", Currency = "JPY" };
        db.Context.Securities.AddRange(shortName, symbolName);
        db.Context.WatchlistItems.AddRange(
            new WatchlistItem { Security = shortName, IsActive = true },
            new WatchlistItem { Security = symbolName, IsActive = true });
        await db.Context.SaveChangesAsync();

        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("""{"articles":[]}"""));
        var provider = CreateProvider(db, handler, new Dictionary<string, string?>
        {
            ["JQUANTS_API_KEY"] = "",
            ["EDINET_API_KEY"] = "",
            ["GDELT_MAX_RECORDS_PER_SECURITY"] = "1"
        });

        var result = await provider.PrefetchAsync(2, CancellationToken.None);

        Assert.Equal(0, handler.CallCount);
        Assert.Contains(result.RequestLogs, log => log.CacheKey == "9432" && log.Function == "ArticleList" && !log.Succeeded && log.ErrorMessage?.Contains("No GDELT-safe search term") == true);
        Assert.Contains(result.RequestLogs, log => log.CacheKey == "7201" && log.Function == "ArticleList" && !log.Succeeded && log.ErrorMessage?.Contains("No GDELT-safe search term") == true);
    }

    [Fact]
    public async Task PrefetchGdeltRequestsLongAsciiSearchTerms()
    {
        using var db = new TestDb();
        var security = new Security { Symbol = "7203", Name = "Toyota Motor", SecurityType = SecurityType.Stock, Country = "JP", Currency = "JPY" };
        db.Context.Securities.Add(security);
        db.Context.WatchlistItems.Add(new WatchlistItem { Security = security, IsActive = true });
        await db.Context.SaveChangesAsync();

        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("""{"articles":[]}"""));
        var provider = CreateProvider(db, handler, new Dictionary<string, string?>
        {
            ["JQUANTS_API_KEY"] = "",
            ["EDINET_API_KEY"] = "",
            ["GDELT_MAX_RECORDS_PER_SECURITY"] = "1"
        });

        await provider.PrefetchAsync(1, CancellationToken.None);

        var query = Uri.UnescapeDataString(Assert.Single(handler.Requests).RequestUri!.Query);
        Assert.Contains("query=\"Toyota Motor\"", query);
        Assert.DoesNotContain("\"7203\"", query);
    }

    [Fact]
    public async Task PrefetchGdeltRequestsJapaneseSearchTerms()
    {
        using var db = new TestDb();
        var companyName = "\u65e5\u7523\u81ea\u52d5\u8eca";
        var security = new Security { Symbol = "7201", Name = companyName, SecurityType = SecurityType.Stock, Country = "JP", Currency = "JPY" };
        db.Context.Securities.Add(security);
        db.Context.WatchlistItems.Add(new WatchlistItem { Security = security, IsActive = true });
        await db.Context.SaveChangesAsync();

        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("""{"articles":[]}"""));
        var provider = CreateProvider(db, handler, new Dictionary<string, string?>
        {
            ["JQUANTS_API_KEY"] = "",
            ["EDINET_API_KEY"] = "",
            ["GDELT_MAX_RECORDS_PER_SECURITY"] = "1"
        });

        await provider.PrefetchAsync(1, CancellationToken.None);

        var query = Uri.UnescapeDataString(Assert.Single(handler.Requests).RequestUri!.Query);
        Assert.Contains($"query=\"{companyName}\"", query);
        Assert.DoesNotContain("\"7201\"", query);
    }

    [Fact]
    public async Task PrefetchGdeltUsesExternalSymbolSearchTerms()
    {
        using var db = new TestDb();
        var security = new Security { Symbol = "7203", Name = "7203", ExternalSymbol = "Toyota Motor", SecurityType = SecurityType.Stock, Country = "JP", Currency = "JPY" };
        db.Context.Securities.Add(security);
        db.Context.WatchlistItems.Add(new WatchlistItem { Security = security, IsActive = true });
        await db.Context.SaveChangesAsync();

        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("""{"articles":[]}"""));
        var provider = CreateProvider(db, handler, new Dictionary<string, string?>
        {
            ["JQUANTS_API_KEY"] = "",
            ["EDINET_API_KEY"] = "",
            ["GDELT_MAX_RECORDS_PER_SECURITY"] = "1"
        });

        await provider.PrefetchAsync(1, CancellationToken.None);

        var query = Uri.UnescapeDataString(Assert.Single(handler.Requests).RequestUri!.Query);
        Assert.Contains("query=\"Toyota Motor\"", query);
        Assert.DoesNotContain("\"7203\"", query);
    }

    [Fact]
    public async Task PrefetchUpdatesSecurityNameFromListedInfoBeforeGdelt()
    {
        using var db = new TestDb();
        var security = new Security { Symbol = "7203", Name = "7203", SecurityType = SecurityType.Stock, Country = "JP", Currency = "JPY" };
        db.Context.Securities.Add(security);
        db.Context.WatchlistItems.Add(new WatchlistItem { Security = security, IsActive = true });
        await db.Context.SaveChangesAsync();

        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("/equities/master", StringComparison.Ordinal)
                ? StubHttpMessageHandler.Json("{\"data\":[{\"Code\":\"72030\",\"CoName\":\"\\u30c8\\u30e8\\u30bf\\u81ea\\u52d5\\u8eca\",\"CoNameEn\":\"Toyota Motor\"}]}")
                : StubHttpMessageHandler.Json("""{"data":[]}"""));
        var provider = CreateProvider(db, handler, new Dictionary<string, string?>
        {
            ["JQUANTS_API_KEY"] = "test-key",
            ["EDINET_API_KEY"] = "",
            ["JQUANTS_RATE_LIMIT_PER_MINUTE"] = "1000000",
            ["GDELT_MAX_RECORDS_PER_SECURITY"] = "1"
        });

        await provider.PrefetchAsync(1, CancellationToken.None);

        var companyName = "\u30c8\u30e8\u30bf\u81ea\u52d5\u8eca";
        Assert.Equal(companyName, security.Name);
        Assert.Equal("Toyota Motor", security.ExternalSymbol);
        var gdeltRequest = handler.Requests.Single(request => request.RequestUri!.Host == "api.gdeltproject.org");
        var query = Uri.UnescapeDataString(gdeltRequest.RequestUri!.Query);
        Assert.Contains($"\"{companyName}\"", query);
        Assert.Contains("\"Toyota Motor\"", query);
        Assert.DoesNotContain("\"7203\"", query);
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
    public async Task ReadsLatestUsableFinancialSnapshotWhenLastStatementRowIsEmpty()
    {
        using var db = new TestDb();
        var security = new Security { Symbol = "7201", Name = "Nissan", SecurityType = SecurityType.Stock, Country = "JP", Currency = "JPY" };
        db.Context.Securities.Add(security);
        await db.Context.SaveChangesAsync();
        db.Context.ExternalApiCacheEntries.Add(Cache("JQuants", "Statements", "7201", """
        {"data":[
            {"DiscDate":"2024-05-09","Sales":"12685716000000","OP":"568718000000","NP":"426649000000","EPS":"110.47","TA":"19855151000000","Eq":"6470543000000","EqAR":"0.301"},
            {"DiscDate":"2024-11-01","DividendForecast":""}
        ]}
        """));
        await db.Context.SaveChangesAsync();

        var provider = CreateProvider(db, new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("{}")), []);

        var financial = await provider.GetFinancialDataAsync(security, CancellationToken.None);

        Assert.True(financial.HasData);
        Assert.Equal(new DateOnly(2024, 5, 9), financial.Snapshot!.DisclosureDate);
        Assert.Equal(12685716000000m, financial.Snapshot.NetSales);
        Assert.Equal(0.301m, financial.Snapshot.EquityRatio);
    }

    [Fact]
    public async Task GetDetailReturnsCachedApiDataForSymbol()
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

        var detail = await provider.GetDetailAsync("7203", CancellationToken.None);

        Assert.True(detail.Found);
        Assert.Equal("Toyota", detail.Name);
        Assert.Single(detail.DailyPrices);
        Assert.Equal(105m, detail.DailyPrices[0].Close);
        Assert.NotNull(detail.FinancialSnapshot);
        Assert.Single(detail.News);
        Assert.Equal(2, detail.CacheEntries.Count);
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
    public async Task PrefetchDoesNotThrottleJQuantsCacheHits()
    {
        using var db = new TestDb();
        var security = new Security { Symbol = "9432", Name = "NTT", SecurityType = SecurityType.Stock, Country = "JP", Currency = "JPY" };
        db.Context.Securities.Add(security);
        db.Context.WatchlistItems.Add(new WatchlistItem { Security = security, IsActive = true });
        db.Context.ExternalApiCacheEntries.AddRange(
            Cache("JQuants", "ListedInfo", "9432", """{"data":[]}"""),
            Cache("JQuants", "DailyQuotes", "9432", """{"data":[]}"""),
            Cache("JQuants", "Statements", "9432", """{"data":[]}"""),
            Cache("JQuants", "EarningsCalendar", "9432", """{"data":[]}"""));
        await db.Context.SaveChangesAsync();

        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("{}"));
        var provider = CreateProvider(db, handler, new Dictionary<string, string?>
        {
            ["JQUANTS_API_KEY"] = "test-key",
            ["EDINET_API_KEY"] = "",
            ["JQUANTS_RATE_LIMIT_PER_MINUTE"] = "1",
            ["GDELT_MAX_RECORDS_PER_SECURITY"] = "1"
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var result = await provider.PrefetchAsync(1, timeout.Token);

        Assert.Equal(0, handler.CallCount);
        Assert.Equal(4, result.RequestLogs.Count(log => log.IsCacheHit));
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

    [Fact]
    public async Task PrefetchGdeltUsesUrlSlugWhenArticleTitleIsMissing()
    {
        using var db = new TestDb();
        var security = new Security { Symbol = "9432", Name = "NTT", ExternalSymbol = "NTT Inc", SecurityType = SecurityType.Stock, Country = "JP", Currency = "JPY" };
        db.Context.Securities.Add(security);
        db.Context.WatchlistItems.Add(new WatchlistItem { Security = security, IsActive = true });
        await db.Context.SaveChangesAsync();

        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("""
        {"articles":[{"url":"https://example.test/news/docomo-becomes-first-in-japan-to-deploy-nokia","title":"","seendate":"20260623T193000Z","domain":"example.test"}]}
        """));
        var provider = CreateProvider(db, handler, new Dictionary<string, string?>
        {
            ["JQUANTS_API_KEY"] = "",
            ["EDINET_API_KEY"] = "",
            ["GDELT_MAX_RECORDS_PER_SECURITY"] = "1"
        });

        await provider.PrefetchAsync(1, CancellationToken.None);

        var news = Assert.Single(db.Context.NewsItems);
        Assert.Equal("docomo becomes first in japan to deploy nokia", news.Title);
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
