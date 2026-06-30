using System.Globalization;
using System.Net;
using System.Text.Json;
using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Application.DTOs;
using InvestmentDecisionBot.Domain.Entities;
using InvestmentDecisionBot.Domain.Enums;
using InvestmentDecisionBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace InvestmentDecisionBot.Infrastructure.Providers;

public sealed class JQuantsMarketDataProvider(
    BotDbContext db,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<JQuantsMarketDataProvider> logger)
    : IMarketDataProvider, ICachedMarketDataProvider, IFinancialDataProvider, IMarketDataPrefetchService
{
    private const string JQuants = "JQuants";
    private const string Edinet = "Edinet";
    private const string Gdelt = "Gdelt";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string jQuantsApiKey = configuration["JQUANTS_API_KEY"] ?? "";
    private readonly string jQuantsBaseUrl = TrimEnd(configuration["JQUANTS_BASE_URL"] ?? "https://api.jquants.com");
    private readonly int jQuantsRateLimit = ReadInt(configuration, "JQUANTS_RATE_LIMIT_PER_MINUTE", 5);
    private readonly int jQuantsDelayWeeks = ReadInt(configuration, "JQUANTS_FREE_DELAY_WEEKS", 12);
    private readonly string edinetApiKey = configuration["EDINET_API_KEY"] ?? "";
    private readonly string edinetBaseUrl = TrimEnd(configuration["EDINET_BASE_URL"] ?? "https://api.edinet-fsa.go.jp/api/v2");
    private readonly int edinetLookbackDays = ReadInt(configuration, "EDINET_LOOKBACK_DAYS", 3);
    private readonly string gdeltBaseUrl = configuration["GDELT_BASE_URL"] ?? "https://api.gdeltproject.org/api/v2/doc/doc";
    private readonly string gdeltTimespan = configuration["GDELT_TIMESPAN"] ?? "7d";
    private readonly int gdeltMaxRecords = ReadInt(configuration, "GDELT_MAX_RECORDS_PER_SECURITY", 50);
    private readonly int gdeltRequestDelayMs = ReadInt(configuration, "GDELT_REQUEST_DELAY_MS", 6000);

    public async Task<MarketPriceResult> GetLatestPriceAsync(Security security, CancellationToken cancellationToken)
    {
        var bars = await GetCachedDailyPricesAsync(security, cancellationToken);
        var latest = bars.OrderBy(bar => bar.Date).LastOrDefault();
        return latest is null
            ? new MarketPriceResult(null, security.Currency, true, true, "J-Quants daily quote cache is missing.")
            : new MarketPriceResult(latest.Close, "JPY", false, true, null);
    }

    public async Task<IReadOnlyList<DailyPriceBar>> GetCachedDailyPricesAsync(Security security, CancellationToken cancellationToken)
    {
        var entries = await db.ExternalApiCacheEntries
            .Where(entry => entry.Provider == JQuants && entry.Function == "DailyQuotes" && entry.CacheKey == security.Symbol && entry.Succeeded)
            .ToListAsync(cancellationToken);

        return entries
            .OrderByDescending(entry => entry.FetchedAt)
            .Take(8)
            .SelectMany(entry => ParseDailyPrices(entry.PayloadJson))
            .GroupBy(bar => bar.Date)
            .Select(group => group.OrderByDescending(_ => _.Date).First())
            .OrderBy(bar => bar.Date)
            .ToList();
    }

    public async Task<IReadOnlyList<NewsSentimentData>> GetCachedNewsAsync(Security security, CancellationToken cancellationToken)
    {
        var news = await db.NewsItems
            .Where(item => item.SecurityId == security.Id)
            .ToListAsync(cancellationToken);

        return news
            .OrderByDescending(item => item.PublishedAt ?? item.FetchedAt)
            .Take(50)
            .Select(item =>
        {
            var sentiment = item.Sentiment?.Equals("Positive", StringComparison.OrdinalIgnoreCase) == true
                ? 0.35m
                : item.Sentiment?.Equals("Negative", StringComparison.OrdinalIgnoreCase) == true
                    ? -0.35m
                    : 0m;
            return new NewsSentimentData(item.Title, item.Url, item.PublishedAt, sentiment, Relevance(item.Title, security), item.Summary, item.Source, item.FetchedAt);
        }).ToList();
    }

    public Task<decimal?> GetCachedExchangeRateAsync(string fromCurrency, string toCurrency, CancellationToken cancellationToken) =>
        Task.FromResult<decimal?>(fromCurrency.Equals(toCurrency, StringComparison.OrdinalIgnoreCase) ? 1m : null);

    public async Task<FinancialDataResult> GetFinancialDataAsync(Security security, CancellationToken cancellationToken)
    {
        var entry = await db.ExternalApiCacheEntries
            .Where(cache => cache.Provider == JQuants && cache.Function == "Statements" && cache.CacheKey == security.Symbol && cache.Succeeded)
            .ToListAsync(cancellationToken);
        var latestEntry = entry.OrderByDescending(cache => cache.FetchedAt).FirstOrDefault();
        if (latestEntry is null)
        {
            return new FinancialDataResult(false, null, "J-Quants statements cache is missing.");
        }

        var snapshot = ParseFinancialSnapshot(latestEntry.PayloadJson);
        return snapshot is null
            ? new FinancialDataResult(false, null, "J-Quants statements cache has no usable fields.")
            : new FinancialDataResult(true, snapshot);
    }

    public async Task<MarketDataStatusResult> GetStatusAsync(CancellationToken cancellationToken)
    {
        var allTargets = (await GetTargetsQuery().ToListAsync(cancellationToken)).Where(IsSupportedJapaneseStock).ToList();
        var targets = allTargets.Count;
        var today = DateTimeOffset.UtcNow.Date;
        var usedToday = (await db.ExternalApiRequestLogs.Where(log => log.Provider == JQuants).ToListAsync(cancellationToken)).Count(log => log.RequestedAt >= today);
        var remaining = Math.Max(0, jQuantsRateLimit * 24 * 60 - usedToday);
        var next = allTargets.OrderBy(security => security.Symbol).Select(security => security.Symbol).Take(5).ToList();

        return new MarketDataStatusResult(jQuantsRateLimit * 24 * 60, usedToday, remaining, targets, next);
    }

    public async Task<MarketDataCoverageResult> GetCoverageAsync(CancellationToken cancellationToken)
    {
        var targets = (await GetTargetsQuery().OrderBy(security => security.Symbol).ToListAsync(cancellationToken)).Where(IsSupportedJapaneseStock).ToList();
        var items = new List<MarketDataCoverageItem>();
        foreach (var security in targets)
        {
            var hasDaily = await HasFreshCacheAsync(JQuants, "DailyQuotes", security.Symbol, cancellationToken);
            var hasFinancial = await HasFreshCacheAsync(JQuants, "Statements", security.Symbol, cancellationToken);
            var hasNews = await db.NewsItems.AnyAsync(item => item.SecurityId == security.Id, cancellationToken);
            var latestPrice = await db.MarketPriceSnapshots
                .Where(snapshot => snapshot.SecurityId == security.Id && snapshot.Price != null && !snapshot.UsedFallback)
                .ToListAsync(cancellationToken);
            var latestSnapshot = latestPrice.OrderByDescending(snapshot => snapshot.FetchedAt).FirstOrDefault();

            items.Add(new MarketDataCoverageItem(
                security.Symbol,
                security.Name,
                await db.Holdings.AnyAsync(h => h.SecurityId == security.Id && h.IsActive, cancellationToken) ? "Holding" : "Watchlist",
                security.ExternalSymbol,
                true,
                latestSnapshot is not null || hasDaily,
                hasDaily,
                hasNews,
                hasFinancial,
                latestSnapshot?.FetchedAt,
                hasDaily ? DateTimeOffset.UtcNow : null,
                hasNews ? DateTimeOffset.UtcNow : null,
                security.ExternalSymbolResolutionError));
        }

        return new MarketDataCoverageResult(
            items.Count,
            items.Count,
            items.Count(item => item.HasFreshPrice),
            items.Count(item => item.HasDailySeries),
            items.Count(item => item.HasNewsSentiment),
            items.Count(item => item.HasExchangeRate),
            items);
    }

    public async Task<MarketDataDetailResult> GetDetailAsync(string symbol, CancellationToken cancellationToken)
    {
        var normalized = symbol.Trim();
        var security = await db.Securities
            .FirstOrDefaultAsync(item => item.Symbol == normalized, cancellationToken);
        if (security is null)
        {
            return new MarketDataDetailResult(false, normalized, null, [], null, [], [], "Security was not found. Import SBI CSV or add it to the watchlist first.");
        }

        var dailyPrices = await GetCachedDailyPricesAsync(security, cancellationToken);
        var financial = await GetFinancialDataAsync(security, cancellationToken);
        var news = await GetCachedNewsAsync(security, cancellationToken);
        var cacheRows = await db.ExternalApiCacheEntries
            .Where(cache => cache.CacheKey == security.Symbol || cache.CacheKey == security.ExternalSymbol)
            .ToListAsync(cancellationToken);
        var cacheEntries = cacheRows
            .OrderByDescending(cache => cache.FetchedAt)
            .Select(cache => new ExternalApiCacheSummary(
                cache.Provider,
                cache.Function,
                cache.CacheKey,
                cache.FetchedAt,
                cache.ExpiresAt,
                cache.Succeeded,
                cache.ErrorMessage,
                cache.PayloadJson.Length))
            .ToList();

        return new MarketDataDetailResult(
            true,
            security.Symbol,
            security.Name,
            dailyPrices,
            financial.Snapshot,
            news,
            cacheEntries,
            financial.ErrorMessage);
    }

    public async Task<MarketDataPrefetchResult> PrefetchAsync(int? limit, CancellationToken cancellationToken)
    {
        var requestedLimit = limit.GetValueOrDefault(20);
        var targets = (await GetTargetsQuery().OrderBy(security => security.Symbol).ToListAsync(cancellationToken))
            .Where(IsSupportedJapaneseStock)
            .Take(Math.Max(0, requestedLimit))
            .ToList();
        var messages = new List<string>();
        var logs = new List<MarketDataRequestLogItem>();
        var attempted = 0;
        var succeeded = 0;
        var skipped = 0;

        if (targets.Count == 0)
        {
            return new MarketDataPrefetchResult(requestedLimit, 0, 0, 0, 0, jQuantsRateLimit * 24 * 60, ["No Japanese stock targets found."], []);
        }

        if (string.IsNullOrWhiteSpace(jQuantsApiKey))
        {
            messages.Add("JQUANTS_API_KEY is not configured. J-Quants prefetch was skipped.");
            skipped += targets.Count;
        }
        else
        {
            foreach (var security in targets)
            {
                foreach (var request in BuildJQuantsRequests(security.Symbol))
                {
                    attempted++;
                    var log = await FetchAndCacheAsync(JQuants, request.Function, security.Symbol, request.Url, jQuantsApiKey, "x-api-key", TimeSpan.FromDays(request.CacheDays), cancellationToken);
                    logs.Add(log);
                    if (log.Succeeded) succeeded++;
                    if (log.Succeeded && request.Function == "ListedInfo")
                    {
                        await db.SaveChangesAsync(cancellationToken);
                        await UpdateSecurityFromListedInfoAsync(security, cancellationToken);
                    }

                    if (!log.IsCacheHit)
                    {
                        await ThrottleAsync(jQuantsRateLimit, cancellationToken);
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(edinetApiKey))
        {
            var edinetLogs = await PrefetchEdinetAsync(targets, cancellationToken);
            logs.AddRange(edinetLogs);
            attempted += edinetLogs.Count;
            succeeded += edinetLogs.Count(log => log.Succeeded);
        }
        else
        {
            messages.Add("EDINET_API_KEY is not configured. EDINET prefetch was skipped.");
        }

        var gdeltLogs = await PrefetchGdeltAsync(targets, cancellationToken);
        logs.AddRange(gdeltLogs);
        attempted += gdeltLogs.Count;
        succeeded += gdeltLogs.Count(log => log.Succeeded);

        await db.SaveChangesAsync(cancellationToken);
        var usedToday = (await db.ExternalApiRequestLogs.Where(log => log.Provider == JQuants).ToListAsync(cancellationToken)).Count(log => log.RequestedAt >= DateTimeOffset.UtcNow.Date);
        return new MarketDataPrefetchResult(requestedLimit, attempted, succeeded, skipped, usedToday, Math.Max(0, jQuantsRateLimit * 24 * 60 - usedToday), messages, logs);
    }

    private IQueryable<Security> GetTargetsQuery()
    {
        var holdingIds = db.Holdings.Where(holding => holding.IsActive).Select(holding => holding.SecurityId);
        var watchIds = db.WatchlistItems.Where(item => item.IsActive).Select(item => item.SecurityId);
        return db.Securities.Where(security =>
            security.SecurityType == SecurityType.Stock &&
            security.Symbol.Length == 4 &&
            (string.IsNullOrEmpty(security.Country) || security.Country == "JP") &&
            (holdingIds.Contains(security.Id) || watchIds.Contains(security.Id)));
    }

    private static bool IsSupportedJapaneseStock(Security security) =>
        security.Symbol.Length == 4 &&
        security.Symbol.All(char.IsDigit) &&
        (string.IsNullOrWhiteSpace(security.Country) || security.Country.Equals("JP", StringComparison.OrdinalIgnoreCase)) &&
        (string.IsNullOrWhiteSpace(security.Currency) || security.Currency.Equals("JPY", StringComparison.OrdinalIgnoreCase));

    private IEnumerable<(string Function, string Url, int CacheDays)> BuildJQuantsRequests(string symbol)
    {
        var to = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-jQuantsDelayWeeks * 7));
        var from = to.AddYears(-2);
        yield return ("ListedInfo", $"{jQuantsBaseUrl}/v2/equities/master?code={Uri.EscapeDataString(symbol)}", 7);
        yield return ("DailyQuotes", $"{jQuantsBaseUrl}/v2/equities/bars/daily?code={Uri.EscapeDataString(symbol)}&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", 1);
        yield return ("Statements", $"{jQuantsBaseUrl}/v2/fins/summary?code={Uri.EscapeDataString(symbol)}", 1);
        yield return ("EarningsCalendar", $"{jQuantsBaseUrl}/v2/equities/earnings-calendar?code={Uri.EscapeDataString(symbol)}", 1);
    }

    private async Task<List<MarketDataRequestLogItem>> PrefetchEdinetAsync(IReadOnlyList<Security> targets, CancellationToken cancellationToken)
    {
        var logs = new List<MarketDataRequestLogItem>();
        var targetSymbols = targets.Select(target => target.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < edinetLookbackDays; i++)
        {
            var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-i));
            var url = $"{edinetBaseUrl}/documents.json?date={date:yyyy-MM-dd}&type=2";
            var log = await FetchAndCacheAsync(Edinet, "DocumentsList", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), url, edinetApiKey, "Subscription-Key", TimeSpan.FromDays(7), cancellationToken);
            logs.Add(log);
            if (!log.Succeeded)
            {
                continue;
            }

            await db.SaveChangesAsync(cancellationToken);
            var entry = await db.ExternalApiCacheEntries.FirstAsync(cache => cache.Provider == Edinet && cache.Function == "DocumentsList" && cache.CacheKey == date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), cancellationToken);
            foreach (var document in ParseEdinetDocuments(entry.PayloadJson).Where(document => targetSymbols.Contains(document.SecurityCode) && document.CsvFlag == "1"))
            {
                if (await HasFreshCacheAsync(Edinet, "DocumentCsvZip", document.DocId, cancellationToken))
                {
                    continue;
                }

                var csvUrl = $"{edinetBaseUrl}/documents/{Uri.EscapeDataString(document.DocId)}?type=5";
                logs.Add(await FetchAndCacheAsync(Edinet, "DocumentCsvZip", document.DocId, csvUrl, edinetApiKey, "Subscription-Key", TimeSpan.FromDays(90), cancellationToken));
            }
        }

        return logs;
    }

    private async Task UpdateSecurityFromListedInfoAsync(Security security, CancellationToken cancellationToken)
    {
        var entry = await db.ExternalApiCacheEntries
            .FirstOrDefaultAsync(cache => cache.Provider == JQuants && cache.Function == "ListedInfo" && cache.CacheKey == security.Symbol && cache.Succeeded, cancellationToken);
        if (entry is null)
        {
            return;
        }

        var listedInfo = ParseListedInfo(entry.PayloadJson, security.Symbol);
        if (!listedInfo.HasValue)
        {
            return;
        }

        var (companyName, companyNameEnglish) = listedInfo.Value;
        var now = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(companyName) && security.Name == security.Symbol)
        {
            security.Name = companyName;
            security.UpdatedAt = now;
        }

        if (!string.IsNullOrWhiteSpace(companyNameEnglish))
        {
            security.ExternalSymbol = companyNameEnglish.Length > 64
                ? companyNameEnglish[..64]
                : companyNameEnglish;
            security.ExternalSymbolResolvedAt = now;
            security.ExternalSymbolResolutionError = null;
            security.UpdatedAt = now;
        }
    }

    private async Task<List<MarketDataRequestLogItem>> PrefetchGdeltAsync(IReadOnlyList<Security> targets, CancellationToken cancellationToken)
    {
        var logs = new List<MarketDataRequestLogItem>();
        foreach (var security in targets)
        {
            var query = BuildGdeltQuery(security);
            if (query is null)
            {
                logs.Add(new MarketDataRequestLogItem(DateTimeOffset.UtcNow, "ArticleList", security.Symbol, false, "No GDELT-safe search term was available."));
                continue;
            }

            var url = $"{gdeltBaseUrl}?query={Uri.EscapeDataString(query)}&mode=ArtList&format=json&timespan={Uri.EscapeDataString(gdeltTimespan)}&maxrecords={Math.Clamp(gdeltMaxRecords, 1, 250)}&sort=DateDesc";
            var log = await FetchAndCacheAsync(Gdelt, "ArticleList", security.Symbol, url, null, null, TimeSpan.FromHours(12), cancellationToken);
            logs.Add(log);
            if (!log.Succeeded)
            {
                await DelayGdeltAsync(cancellationToken);
                continue;
            }

            await db.SaveChangesAsync(cancellationToken);
            var entry = await db.ExternalApiCacheEntries.FirstAsync(cache => cache.Provider == Gdelt && cache.Function == "ArticleList" && cache.CacheKey == security.Symbol, cancellationToken);
            foreach (var article in ParseGdeltArticles(entry.PayloadJson, security))
            {
                var exists = await db.NewsItems.AnyAsync(item => item.SecurityId == security.Id && item.Url == article.Url, cancellationToken);
                if (!exists)
                {
                    db.NewsItems.Add(article);
                }
            }

            await DelayGdeltAsync(cancellationToken);
        }

        return logs;
    }

    private Task DelayGdeltAsync(CancellationToken cancellationToken) =>
        gdeltRequestDelayMs > 0 ? Task.Delay(gdeltRequestDelayMs, cancellationToken) : Task.CompletedTask;

    private static string? BuildGdeltQuery(Security security)
    {
        var terms = new[] { security.Name, security.ExternalSymbol, security.Symbol }
            .Select(NormalizeGdeltSearchTerm)
            .Where(term => term is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(term => $"\"{term}\"")
            .ToList();

        return terms.Count switch
        {
            0 => null,
            1 => terms[0],
            _ => $"({string.Join(" OR ", terms)})"
        };
    }

    private static string? NormalizeGdeltSearchTerm(string? value)
    {
        var normalized = string.Join(' ', (value ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length <= 3 || normalized.All(char.IsDigit) || !normalized.Any(char.IsLetter) || normalized.Any(IsHalfWidthKanaOrPunctuation))
        {
            return null;
        }

        return normalized.Replace("\"", "", StringComparison.Ordinal);
    }

    private static bool IsHalfWidthKanaOrPunctuation(char value) =>
        value is >= '\uFF61' and <= '\uFF9F';

    private async Task<MarketDataRequestLogItem> FetchAndCacheAsync(string provider, string function, string cacheKey, string url, string? apiKey, string? apiKeyHeader, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var cached = (await db.ExternalApiCacheEntries
            .Where(cache => cache.Provider == provider && cache.Function == function && cache.CacheKey == cacheKey && cache.Succeeded)
            .ToListAsync(cancellationToken))
            .FirstOrDefault(cache => cache.ExpiresAt > now);
        if (cached is not null)
        {
            try
            {
                if (ExpectsJsonPayload(provider, function))
                {
                    ValidateJsonPayload(cached.PayloadJson, provider, function, cacheKey);
                }

                return new MarketDataRequestLogItem(now, function, cacheKey, true, "cache hit", true);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Cached external API payload is invalid for {Provider} {Function} {CacheKey}. Refetching.", provider, function, cacheKey);
                cached.Succeeded = false;
                cached.ErrorMessage = ex.Message;
                cached.ExpiresAt = now;
                cached.UpdatedAt = now;
            }
        }

        var requestLog = new ExternalApiRequestLog { Provider = provider, Function = function, CacheKey = cacheKey, RequestedAt = now };
        try
        {
            var payload = await SendWithRetryAsync(url, apiKey, apiKeyHeader, cancellationToken);
            if (ExpectsJsonPayload(provider, function))
            {
                ValidateJsonPayload(payload, provider, function, cacheKey);
            }

            await UpsertCacheAsync(provider, function, cacheKey, payload, now, now.Add(ttl), true, null, cancellationToken);
            requestLog.Succeeded = true;
            db.ExternalApiRequestLogs.Add(requestLog);
            return new MarketDataRequestLogItem(now, function, cacheKey, true, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "External API request failed for {Provider} {Function} {CacheKey}.", provider, function, cacheKey);
            await UpsertCacheAsync(provider, function, cacheKey, "", now, now.AddMinutes(30), false, ex.Message, cancellationToken);
            requestLog.Succeeded = false;
            requestLog.ErrorMessage = ex.Message;
            db.ExternalApiRequestLogs.Add(requestLog);
            return new MarketDataRequestLogItem(now, function, cacheKey, false, ex.Message);
        }
    }

    private async Task<string> SendWithRetryAsync(string url, string? apiKey, string? apiKeyHeader, CancellationToken cancellationToken)
    {
        var http = httpClientFactory.CreateClient("ExternalMarketData");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(apiKeyHeader))
            {
                request.Headers.TryAddWithoutValidation(apiKeyHeader, apiKey);
            }

            using var response = await http.SendAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.TooManyRequests && (int)response.StatusCode < 500)
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }

            var retryAfter = response.Headers.RetryAfter?.Delta ?? response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow;
            var retryDelay = response.StatusCode == HttpStatusCode.TooManyRequests
                ? TimeSpan.FromSeconds(6)
                : TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
            if (retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero && retryAfter.Value > retryDelay)
            {
                retryDelay = retryAfter.Value;
            }

            await Task.Delay(retryDelay, cancellationToken);
        }

        throw new HttpRequestException("External API request failed after retries.");
    }

    private async Task UpsertCacheAsync(string provider, string function, string cacheKey, string payload, DateTimeOffset fetchedAt, DateTimeOffset expiresAt, bool succeeded, string? error, CancellationToken cancellationToken)
    {
        var entry = await db.ExternalApiCacheEntries.FirstOrDefaultAsync(cache => cache.Provider == provider && cache.Function == function && cache.CacheKey == cacheKey, cancellationToken);
        if (entry is null)
        {
            entry = new ExternalApiCacheEntry { Provider = provider, Function = function, CacheKey = cacheKey, CreatedAt = fetchedAt };
            db.ExternalApiCacheEntries.Add(entry);
        }

        entry.PayloadJson = payload;
        entry.FetchedAt = fetchedAt;
        entry.ExpiresAt = expiresAt;
        entry.Succeeded = succeeded;
        entry.ErrorMessage = error;
        entry.UpdatedAt = fetchedAt;
    }

    private static bool ExpectsJsonPayload(string provider, string function) =>
        !(provider == Edinet && function == "DocumentCsvZip");

    private static void ValidateJsonPayload(string payload, string provider, string function, string cacheKey)
    {
        try
        {
            using var _ = JsonDocument.Parse(payload);
        }
        catch (JsonException ex)
        {
            var sample = payload.Length <= 120 ? payload : payload[..120];
            throw new JsonException($"{provider} {function} {cacheKey} returned a non-JSON payload: {sample}", ex);
        }
    }

    private async Task<bool> HasFreshCacheAsync(string provider, string function, string cacheKey, CancellationToken cancellationToken) =>
        (await db.ExternalApiCacheEntries
            .Where(cache => cache.Provider == provider && cache.Function == function && cache.CacheKey == cacheKey && cache.Succeeded)
            .ToListAsync(cancellationToken))
            .Any(cache => cache.ExpiresAt > DateTimeOffset.UtcNow);

    private static IReadOnlyList<DailyPriceBar> ParseDailyPrices(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var array = FindArray(document.RootElement, "daily_quotes", "dailyQuotes", "prices", "data");
        if (array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var bars = new List<DailyPriceBar>();
        foreach (var item in array.EnumerateArray())
        {
            if (!TryGetDate(item, out var date) || !TryGetDecimal(item, out var close, "Close", "AdjustmentClose", "C", "AdjC"))
            {
                continue;
            }

            bars.Add(new DailyPriceBar(
                date,
                GetDecimal(item, "Open", "AdjustmentOpen", "O", "AdjO") ?? close,
                GetDecimal(item, "High", "AdjustmentHigh", "H", "AdjH") ?? close,
                GetDecimal(item, "Low", "AdjustmentLow", "L", "AdjL") ?? close,
                close,
                GetLong(item, "Volume", "TurnoverValue", "Vo", "AdjVo") ?? 0));
        }

        return bars;
    }

    private static FinancialSnapshotData? ParseFinancialSnapshot(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var array = FindArray(document.RootElement, "statements", "Statements", "data");

        if (array.ValueKind == JsonValueKind.Array)
        {
            return array.EnumerateArray()
                .Select(ParseFinancialSnapshotItem)
                .Where(snapshot => snapshot is not null)
                .OrderBy(snapshot => snapshot!.DisclosureDate ?? DateOnly.MinValue)
                .LastOrDefault();
        }

        return ParseFinancialSnapshotItem(document.RootElement);
    }

    private static FinancialSnapshotData? ParseFinancialSnapshotItem(JsonElement item)
    {
        if (item.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        var snapshot = new FinancialSnapshotData(
            TryGetDate(item, out var date) ? date : null,
            GetDecimal(item, "NetSales", "Sales", "OperatingRevenue"),
            GetDecimal(item, "OperatingProfit", "OperatingIncome", "OP"),
            GetDecimal(item, "OrdinaryProfit", "OdP"),
            GetDecimal(item, "Profit", "NetIncome", "ProfitAttributableToOwnersOfParent", "NP"),
            GetDecimal(item, "EarningsPerShare", "EPS"),
            GetDecimal(item, "BookValuePerShare", "BPS"),
            GetDecimal(item, "TotalAssets", "TA"),
            GetDecimal(item, "NetAssets", "Equity", "Eq"),
            GetDecimal(item, "EquityToAssetRatio", "EquityRatio", "EqAR"));

        return snapshot.NetSales is null && snapshot.OperatingProfit is null && snapshot.Profit is null && snapshot.EquityRatio is null
            ? null
            : snapshot;
    }

    private static (string? CompanyName, string? CompanyNameEnglish)? ParseListedInfo(string payload, string symbol)
    {
        using var document = JsonDocument.Parse(payload);
        var array = FindArray(document.RootElement, "info", "listed_info", "listedInfo", "data");
        var items = array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().ToList()
            : [document.RootElement];

        foreach (var item in items)
        {
            var code = GetString(item, "Code", "code", "LocalCode", "localCode") ?? "";
            if (!string.IsNullOrWhiteSpace(code) && !code.StartsWith(symbol, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var companyName = FirstNonEmpty(
                GetString(item, "CoName", "coName"),
                GetString(item, "CompanyName", "companyName"),
                GetString(item, "Name", "name"),
                GetString(item, "CompanyNameFull", "companyNameFull"));
            var companyNameEnglish = FirstNonEmpty(
                GetString(item, "CoNameEn", "coNameEn"),
                GetString(item, "CompanyNameEnglish", "companyNameEnglish"),
                GetString(item, "EnglishName", "englishName"),
                GetString(item, "CompanyNameEng", "companyNameEng"));

            if (!string.IsNullOrWhiteSpace(companyName) || !string.IsNullOrWhiteSpace(companyNameEnglish))
            {
                return (companyName, companyNameEnglish);
            }
        }

        return null;
    }

    private static IEnumerable<(string DocId, string SecurityCode, string CsvFlag)> ParseEdinetDocuments(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var array = FindArray(document.RootElement, "results", "documents", "data");
        if (array.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in array.EnumerateArray())
        {
            var docId = GetString(item, "docID", "docId") ?? "";
            var code = (GetString(item, "secCode", "securityCode") ?? "").Trim();
            if (code.Length > 4)
            {
                code = code[..4];
            }

            if (!string.IsNullOrWhiteSpace(docId) && code.Length == 4)
            {
                yield return (docId, code, GetString(item, "csvFlag") ?? "");
            }
        }
    }

    private static IEnumerable<NewsItem> ParseGdeltArticles(string payload, Security security)
    {
        using var document = JsonDocument.Parse(payload);
        var array = FindArray(document.RootElement, "articles", "ArticleList", "data");
        if (array.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in array.EnumerateArray())
        {
            var url = GetString(item, "url", "URL");
            var title = FirstNonEmpty(GetString(item, "title", "Title"), BuildTitleFromUrl(url)) ?? "";
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title) || !seen.Add(url))
            {
                continue;
            }

            yield return new NewsItem
            {
                SecurityId = security.Id,
                Title = title,
                Url = url,
                PublishedAt = ParseGdeltDate(GetString(item, "seendate", "SeenDate", "publishedAt")),
                Source = GetString(item, "sourcecountry", "domain", "source") ?? Gdelt,
                Sentiment = ClassifySentiment(title),
                FetchedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow
            };
        }
    }

    private static string? BuildTitleFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var slug = uri.Segments
            .Reverse()
            .Select(segment => Uri.UnescapeDataString(segment.Trim('/')))
            .FirstOrDefault(segment => segment.Length > 8 && segment.Any(char.IsLetter));
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        return string.Join(' ', slug
            .Replace('-', ' ')
            .Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Trim();
    }

    private static JsonElement FindArray(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array)
            {
                return value;
            }
        }

        return default;
    }

    private static bool TryGetDate(JsonElement item, out DateOnly date)
    {
        foreach (var name in new[] { "Date", "date", "DisclosedDate", "disclosedDate", "DiscDate" })
        {
            var value = GetString(item, name);
            if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                return true;
            }
        }

        date = default;
        return false;
    }

    private static decimal? GetDecimal(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (!item.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out number))
            {
                return number;
            }
        }

        return null;
    }

    private static bool TryGetDecimal(JsonElement item, out decimal value, params string[] names)
    {
        var result = GetDecimal(item, names);
        value = result.GetValueOrDefault();
        return result is not null;
    }

    private static long? GetLong(JsonElement item, params string[] names)
    {
        var value = GetDecimal(item, names);
        return value is null ? null : decimal.ToInt64(value.Value);
    }

    private static string? GetString(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (item.TryGetProperty(name, out var value))
            {
                return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
            }
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static DateTimeOffset? ParseGdeltDate(string? value)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        if (DateTimeOffset.TryParseExact(value, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string ClassifySentiment(string title)
    {
        var positive = new[] { "上方修正", "増益", "自社株買", "受注", "提携", "最高益", "upgrade", "buyback", "profit" };
        var negative = new[] { "下方修正", "減益", "赤字", "不正", "訴訟", "処分", "情報漏洩", "リコール", "downgrade", "loss", "recall" };
        if (negative.Any(word => title.Contains(word, StringComparison.OrdinalIgnoreCase))) return "Negative";
        if (positive.Any(word => title.Contains(word, StringComparison.OrdinalIgnoreCase))) return "Positive";
        return "Neutral";
    }

    private static decimal Relevance(string title, Security security) =>
        title.Contains(security.Name, StringComparison.OrdinalIgnoreCase) || title.Contains(security.Symbol, StringComparison.OrdinalIgnoreCase) ? 0.85m : 0.45m;

    private static Task ThrottleAsync(int requestsPerMinute, CancellationToken cancellationToken)
    {
        var delayMs = requestsPerMinute <= 0 ? 1000 : (int)Math.Ceiling(60000m / requestsPerMinute);
        return Task.Delay(Math.Min(delayMs, 15000), cancellationToken);
    }

    private static int ReadInt(IConfiguration configuration, string key, int fallback) =>
        int.TryParse(configuration[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static string TrimEnd(string value) => value.TrimEnd('/');
}
