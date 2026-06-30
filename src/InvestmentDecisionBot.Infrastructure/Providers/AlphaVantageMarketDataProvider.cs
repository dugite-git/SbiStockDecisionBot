using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Application.DTOs;
using InvestmentDecisionBot.Domain.Entities;
using InvestmentDecisionBot.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InvestmentDecisionBot.Infrastructure.Providers;

public sealed class AlphaVantageMarketDataProvider(
    HttpClient httpClient,
    IBotDbContext db,
    IOptions<AlphaVantageOptions> options) : IMarketDataProvider, ICachedMarketDataProvider, IMarketDataPrefetchService
{
    private const string ProviderName = "AlphaVantage";
    private static readonly SemaphoreSlim RequestRateGate = new(1, 1);
    private static DateTimeOffset lastRequestStartedAt = DateTimeOffset.MinValue;
    private readonly AlphaVantageOptions options = options.Value;

    public async Task<MarketPriceResult> GetLatestPriceAsync(Security security, CancellationToken cancellationToken)
    {
        if (!options.FetchOnReport)
        {
            return await GetCachedPriceAsync(security, cancellationToken)
                ?? new MarketPriceResult(null, security.Currency, true, true, "レポート実行時のAlpha Vantage取得は無効で、同日中の有効な価格キャッシュもありません。");
        }

        return await FetchLatestPriceAsync(security, cancellationToken);
    }

    public async Task<IReadOnlyList<DailyPriceBar>> GetCachedDailyPricesAsync(Security security, CancellationToken cancellationToken)
    {
        var symbol = security.AlphaVantageSymbol;
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return [];
        }

        var cache = await GetUsableCacheAsync("TIME_SERIES_DAILY", symbol, allowExpired: true, cancellationToken);
        return cache is null ? [] : ParseDailyPrices(cache.PayloadJson);
    }

    public async Task<IReadOnlyList<NewsSentimentData>> GetCachedNewsAsync(Security security, CancellationToken cancellationToken)
    {
        var symbol = security.AlphaVantageSymbol;
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return [];
        }

        var cache = await GetUsableCacheAsync("NEWS_SENTIMENT", symbol, allowExpired: true, cancellationToken);
        return cache is null ? [] : ParseNews(cache.PayloadJson, symbol);
    }

    public async Task<decimal?> GetCachedExchangeRateAsync(string fromCurrency, string toCurrency, CancellationToken cancellationToken)
    {
        var cacheKey = $"{fromCurrency.Trim().ToUpperInvariant()}_{toCurrency.Trim().ToUpperInvariant()}";
        var cache = await GetUsableCacheAsync("CURRENCY_EXCHANGE_RATE", cacheKey, allowExpired: true, cancellationToken);
        if (cache is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(cache.PayloadJson);
        return document.RootElement.TryGetProperty("Realtime Currency Exchange Rate", out var rate) &&
               TryGetDecimal(rate, "5. Exchange Rate", out var value)
            ? value
            : null;
    }

    public async Task<MarketDataStatusResult> GetStatusAsync(CancellationToken cancellationToken)
    {
        var used = await CountRequestsTodayAsync(cancellationToken);
        var pending = await BuildQueueAsync(cancellationToken);
        return new MarketDataStatusResult(
            options.DailyRequestLimit,
            used,
            Math.Max(0, options.DailyRequestLimit - used),
            pending.Count,
            pending.Take(10).Select(item => $"{item.Function}:{item.Security.Symbol}").ToList());
    }

    public async Task<MarketDataPrefetchResult> PrefetchAsync(int? limit, CancellationToken cancellationToken)
    {
        var messages = new List<string>();
        var used = await CountRequestsTodayAsync(cancellationToken);
        var remaining = Math.Max(0, options.DailyRequestLimit - used);
        var requested = Math.Max(0, limit ?? remaining);
        var allowed = Math.Min(requested, remaining);
        var queue = await BuildQueueAsync(cancellationToken);
        var attempted = 0;
        var succeeded = 0;
        var skipped = 0;
        var apiAttempts = 0;

        foreach (var item in queue)
        {
            var consumesApiBudget = item.Function != "SYMBOL_RESOLVE";
            if (consumesApiBudget)
            {
                if (apiAttempts >= allowed)
                {
                    break;
                }

                apiAttempts++;
            }

            attempted++;
            var ok = item.Function switch
            {
                "SYMBOL_RESOLVE" => (await ResolveSymbolAsync(item.Security, allowFetch: true, cancellationToken)).Symbol is not null,
                "GLOBAL_QUOTE" => (await FetchLatestPriceAsync(item.Security, cancellationToken)).Price is not null,
                "TIME_SERIES_DAILY" => await FetchDailyAsync(item.Security, cancellationToken),
                "NEWS_SENTIMENT" => await FetchNewsAsync(item.Security, cancellationToken),
                _ => false
            };

            if (ok) succeeded++; else skipped++;
        }

        if (allowed == 0 && attempted == 0)
        {
            messages.Add("本日のAlpha Vantageリクエスト残数がありません。");
        }

        used = await CountRequestsTodayAsync(cancellationToken);
        return new MarketDataPrefetchResult(requested, attempted, succeeded, skipped, used, Math.Max(0, options.DailyRequestLimit - used), messages);
    }

    public async Task<MarketDataCoverageResult> GetCoverageAsync(CancellationToken cancellationToken)
    {
        var targets = await BuildCoverageTargetsAsync(cancellationToken);
        var securityIds = targets.Select(target => target.Security.Id).ToHashSet();
        var symbols = targets
            .Select(target => target.Security.AlphaVantageSymbol)
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(symbol => symbol!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var priceSnapshots = await db.MarketPriceSnapshots
            .Where(snapshot => securityIds.Contains(snapshot.SecurityId) && snapshot.Price != null && !snapshot.UsedFallback)
            .ToListAsync(cancellationToken);
        var cacheEntries = await db.ExternalApiCacheEntries
            .Where(entry => entry.Provider == ProviderName && entry.Succeeded)
            .ToListAsync(cancellationToken);

        var items = new List<MarketDataCoverageItem>();
        foreach (var target in targets)
        {
            var security = target.Security;
            var alphaSymbol = security.AlphaVantageSymbol;
            var symbolCaches = string.IsNullOrWhiteSpace(alphaSymbol)
                ? []
                : cacheEntries.Where(entry => entry.CacheKey.Equals(alphaSymbol, StringComparison.OrdinalIgnoreCase)).ToList();
            var latestPrice = priceSnapshots
                .Where(snapshot => snapshot.SecurityId == security.Id)
                .OrderByDescending(snapshot => snapshot.FetchedAt)
                .FirstOrDefault();
            var quoteCache = symbolCaches.Where(entry => entry.Function == "GLOBAL_QUOTE").OrderByDescending(entry => entry.FetchedAt).FirstOrDefault();
            var dailyCache = symbolCaches.Where(entry => entry.Function == "TIME_SERIES_DAILY").OrderByDescending(entry => entry.FetchedAt).FirstOrDefault();
            var newsCache = symbolCaches.Where(entry => entry.Function == "NEWS_SENTIMENT").OrderByDescending(entry => entry.FetchedAt).FirstOrDefault();
            var hasExchangeRate = !string.Equals(security.Currency, "USD", StringComparison.OrdinalIgnoreCase) ||
                                  cacheEntries.Any(entry => entry.Function == "CURRENCY_EXCHANGE_RATE" && entry.CacheKey.Equals("USD_JPY", StringComparison.OrdinalIgnoreCase));

            items.Add(new MarketDataCoverageItem(
                security.Symbol,
                security.Name,
                target.TargetType,
                alphaSymbol,
                !string.IsNullOrWhiteSpace(alphaSymbol),
                latestPrice is not null || quoteCache is not null,
                dailyCache is not null,
                newsCache is not null,
                hasExchangeRate,
                latestPrice?.FetchedAt ?? quoteCache?.FetchedAt,
                dailyCache?.FetchedAt,
                newsCache?.FetchedAt,
                security.AlphaVantageSymbolResolutionError));
        }

        return new MarketDataCoverageResult(
            items.Count,
            items.Count(item => item.IsAlphaVantageCovered),
            items.Count(item => item.HasFreshPrice),
            items.Count(item => item.HasDailySeries),
            items.Count(item => item.HasNewsSentiment),
            items.Count(item => item.HasExchangeRate),
            items);
    }

    private async Task<MarketPriceResult?> GetCachedPriceAsync(Security security, CancellationToken cancellationToken)
    {
        var today = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);
        var tomorrow = today.AddDays(1);
        var reusableSnapshots = await db.MarketPriceSnapshots
            .Where(snapshot =>
                snapshot.SecurityId == security.Id &&
                snapshot.Price != null &&
                !snapshot.UsedFallback)
            .ToListAsync(cancellationToken);
        var cached = reusableSnapshots
            .Where(snapshot => snapshot.FetchedAt >= today && snapshot.FetchedAt < tomorrow)
            .OrderByDescending(snapshot => snapshot.FetchedAt)
            .FirstOrDefault();

        return cached is null ? null : new MarketPriceResult(cached.Price, cached.Currency, false, cached.IsStale, null);
    }

    private async Task<MarketPriceResult> FetchLatestPriceAsync(Security security, CancellationToken cancellationToken)
    {
        if (!IsEnabled(out var disabledMessage))
        {
            return new MarketPriceResult(null, security.Currency, true, true, disabledMessage);
        }

        var cached = await GetCachedPriceAsync(security, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var resolved = await ResolveSymbolAsync(security, allowFetch: true, cancellationToken);
        if (resolved.Symbol is null)
        {
            await SavePriceSnapshotAsync(security, null, security.Currency, true, resolved.ErrorMessage, cancellationToken);
            return new MarketPriceResult(null, security.Currency, true, true, resolved.ErrorMessage);
        }

        var payload = await GetOrFetchJsonAsync("GLOBAL_QUOTE", resolved.Symbol, $"function=GLOBAL_QUOTE&symbol={Uri.EscapeDataString(resolved.Symbol)}", GetTtl("GLOBAL_QUOTE"), cancellationToken);
        if (!payload.Succeeded)
        {
            await SavePriceSnapshotAsync(security, null, security.Currency, true, payload.ErrorMessage, cancellationToken);
            return new MarketPriceResult(null, security.Currency, true, true, payload.ErrorMessage);
        }

        using var document = JsonDocument.Parse(payload.PayloadJson);
        if (!document.RootElement.TryGetProperty("Global Quote", out var quote) ||
            !TryGetDecimal(quote, "05. price", out var price))
        {
            await SavePriceSnapshotAsync(security, null, security.Currency, true, "Alpha Vantageレスポンスに利用可能な価格が含まれていません。", cancellationToken);
            return new MarketPriceResult(null, security.Currency, true, true, "Alpha Vantageレスポンスに利用可能な価格が含まれていません。");
        }

        await SavePriceSnapshotAsync(security, price, resolved.Currency ?? security.Currency, false, null, cancellationToken);
        return new MarketPriceResult(price, resolved.Currency ?? security.Currency, false, false, null);
    }

    private async Task<bool> FetchDailyAsync(Security security, CancellationToken cancellationToken)
    {
        var resolved = await ResolveSymbolAsync(security, allowFetch: true, cancellationToken);
        if (resolved.Symbol is null) return false;
        var payload = await GetOrFetchJsonAsync("TIME_SERIES_DAILY", resolved.Symbol, $"function=TIME_SERIES_DAILY&outputsize=compact&symbol={Uri.EscapeDataString(resolved.Symbol)}", GetTtl("TIME_SERIES_DAILY"), cancellationToken);
        return payload.Succeeded;
    }

    private async Task<bool> FetchNewsAsync(Security security, CancellationToken cancellationToken)
    {
        var resolved = await ResolveSymbolAsync(security, allowFetch: true, cancellationToken);
        if (resolved.Symbol is null) return false;
        var payload = await GetOrFetchJsonAsync("NEWS_SENTIMENT", resolved.Symbol, $"function=NEWS_SENTIMENT&tickers={Uri.EscapeDataString(resolved.Symbol)}&limit=20", GetTtl("NEWS_SENTIMENT"), cancellationToken);
        return payload.Succeeded;
    }

    private async Task<SymbolSearchResult> ResolveSymbolAsync(Security security, bool allowFetch, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(security.AlphaVantageSymbol))
        {
            return new SymbolSearchResult(security.AlphaVantageSymbol, security.Currency, null);
        }

        if (!allowFetch)
        {
            return new SymbolSearchResult(null, security.Currency, "Alpha Vantage用シンボルがまだ解決されていません。");
        }

        var normalized = security.Symbol.Trim().ToUpperInvariant();
        var resolved = ResolveSupportedStockSymbol(security, normalized);
        if (resolved.Symbol is null)
        {
            security.AlphaVantageSymbolResolutionError = resolved.ErrorMessage;
            security.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return new SymbolSearchResult(null, security.Currency, security.AlphaVantageSymbolResolutionError);
        }

        security.AlphaVantageSymbol = resolved.Symbol;
        security.Currency = resolved.Currency ?? security.Currency;
        security.Country = resolved.Symbol.EndsWith(".T", StringComparison.OrdinalIgnoreCase) ? "JP" : "US";
        security.AlphaVantageSymbolResolvedAt = DateTimeOffset.UtcNow;
        security.AlphaVantageSymbolResolutionError = null;
        security.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return resolved;
    }

    private async Task<CachedPayload> GetOrFetchJsonAsync(string function, string cacheKey, string queryString, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var cached = await GetUsableCacheAsync(function, cacheKey, allowExpired: false, cancellationToken);
        if (cached is not null)
        {
            return new CachedPayload(cached.PayloadJson, cached.Succeeded, cached.ErrorMessage);
        }

        if (await CountRequestsTodayAsync(cancellationToken) >= options.DailyRequestLimit)
        {
            return new CachedPayload("", false, "Alpha Vantageの日次リクエスト上限に達しました。");
        }

        try
        {
            var separator = options.BaseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            var url = $"{options.BaseUrl}{separator}{queryString}&apikey={Uri.EscapeDataString(options.ApiKey)}";
            await WaitForRequestSlotAsync(cancellationToken);
            using var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return await SaveCacheAsync(function, cacheKey, "", false, $"Alpha VantageがHTTP {(int)response.StatusCode} を返しました。", TimeSpan.FromMinutes(options.FailureCacheMinutes), cancellationToken);
            }

            var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var payloadJson = payload.GetRawText();
            var succeeded = payload.ValueKind == JsonValueKind.Object &&
                            !TryGetMessage(payload, "Error Message", out _) &&
                            !TryGetMessage(payload, "Information", out _) &&
                            !TryGetMessage(payload, "Note", out _);
            var error = succeeded ? null : GetAlphaVantageMessage(payload);
            return await SaveCacheAsync(function, cacheKey, payloadJson, succeeded, error, succeeded ? ttl : TimeSpan.FromMinutes(options.FailureCacheMinutes), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return await SaveCacheAsync(function, cacheKey, "", false, exception.Message, TimeSpan.FromMinutes(options.FailureCacheMinutes), cancellationToken);
        }
    }

    private async Task WaitForRequestSlotAsync(CancellationToken cancellationToken)
    {
        var minimumInterval = TimeSpan.FromMilliseconds(Math.Max(0, options.MinimumRequestIntervalMilliseconds));
        await RequestRateGate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var elapsed = now - lastRequestStartedAt;
            if (elapsed < minimumInterval)
            {
                await Task.Delay(minimumInterval - elapsed, cancellationToken);
            }

            lastRequestStartedAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            RequestRateGate.Release();
        }
    }

    private async Task<CachedPayload> SaveCacheAsync(string function, string cacheKey, string payloadJson, bool succeeded, string? error, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        db.ExternalApiRequestLogs.Add(new ExternalApiRequestLog
        {
            Provider = ProviderName,
            Function = function,
            CacheKey = cacheKey,
            RequestedAt = now,
            Succeeded = succeeded,
            ErrorMessage = error
        });

        var entry = await db.ExternalApiCacheEntries.SingleOrDefaultAsync(e =>
            e.Provider == ProviderName &&
            e.Function == function &&
            e.CacheKey == cacheKey,
            cancellationToken);

        if (entry is null)
        {
            entry = new ExternalApiCacheEntry { Provider = ProviderName, Function = function, CacheKey = cacheKey, CreatedAt = now };
            db.ExternalApiCacheEntries.Add(entry);
        }

        entry.PayloadJson = payloadJson;
        entry.FetchedAt = now;
        entry.ExpiresAt = now.Add(ttl);
        entry.Succeeded = succeeded;
        entry.ErrorMessage = error;
        entry.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return new CachedPayload(payloadJson, succeeded, error);
    }

    private async Task<ExternalApiCacheEntry?> GetUsableCacheAsync(string function, string cacheKey, bool allowExpired, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var entries = await db.ExternalApiCacheEntries
            .Where(e =>
                e.Provider == ProviderName &&
                e.Function == function &&
                e.CacheKey == cacheKey &&
                e.Succeeded)
            .ToListAsync(cancellationToken);

        return entries
            .Where(e => allowExpired || e.ExpiresAt > now)
            .OrderByDescending(e => e.FetchedAt)
            .FirstOrDefault();
    }

    private async Task SavePriceSnapshotAsync(Security security, decimal? price, string? currency, bool failed, string? error, CancellationToken cancellationToken)
    {
        db.MarketPriceSnapshots.Add(new MarketPriceSnapshot
        {
            SecurityId = security.Id,
            Price = price,
            Currency = currency ?? security.Currency,
            FetchedAt = DateTimeOffset.UtcNow,
            DataSource = ProviderName,
            IsStale = failed,
            UsedFallback = failed,
            ErrorMessage = error,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<int> CountRequestsTodayAsync(CancellationToken cancellationToken)
    {
        var today = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);
        var tomorrow = today.AddDays(1);
        var logs = await db.ExternalApiRequestLogs.Where(log => log.Provider == ProviderName)
            .ToListAsync(cancellationToken);
        return logs.Count(log => log.RequestedAt >= today && log.RequestedAt < tomorrow);
    }

    private async Task<List<PrefetchItem>> BuildQueueAsync(CancellationToken cancellationToken)
    {
        var securities = await GetTargetsQuery().ToListAsync(cancellationToken);
        var queue = new List<PrefetchItem>();
        var now = DateTimeOffset.UtcNow;
        foreach (var security in securities)
        {
            if (string.IsNullOrWhiteSpace(security.AlphaVantageSymbol))
            {
                queue.Add(new PrefetchItem("SYMBOL_RESOLVE", security));
                continue;
            }

            if (!await HasFreshCacheAsync("GLOBAL_QUOTE", security.AlphaVantageSymbol, now, cancellationToken))
                queue.Add(new PrefetchItem("GLOBAL_QUOTE", security));
            if (!await HasFreshCacheAsync("TIME_SERIES_DAILY", security.AlphaVantageSymbol, now, cancellationToken))
                queue.Add(new PrefetchItem("TIME_SERIES_DAILY", security));
            if (!await HasFreshCacheAsync("NEWS_SENTIMENT", security.AlphaVantageSymbol, now, cancellationToken))
                queue.Add(new PrefetchItem("NEWS_SENTIMENT", security));
        }

        return queue;
    }

    private IQueryable<Security> GetTargetsQuery()
    {
        var holdingSecurityIds = db.Holdings.Where(h => h.IsActive).Select(h => h.SecurityId);
        var watchSecurityIds = db.WatchlistItems.Where(w => w.IsActive).Select(w => w.SecurityId);
        var ids = holdingSecurityIds.Concat(watchSecurityIds).Distinct();
        return db.Securities.Where(security => ids.Contains(security.Id)).OrderBy(security => security.Symbol);
    }

    private async Task<List<CoverageTarget>> BuildCoverageTargetsAsync(CancellationToken cancellationToken)
    {
        var holdings = await db.Holdings
            .Include(holding => holding.Security)
            .Where(holding => holding.IsActive)
            .Select(holding => new CoverageTarget(holding.Security, "Holding"))
            .ToListAsync(cancellationToken);
        var holdingIds = holdings.Select(target => target.Security.Id).ToHashSet();
        var watchlist = await db.WatchlistItems
            .Include(item => item.Security)
            .Where(item => item.IsActive && !holdingIds.Contains(item.SecurityId))
            .Select(item => new CoverageTarget(item.Security, "Watchlist"))
            .ToListAsync(cancellationToken);

        return holdings
            .Concat(watchlist)
            .OrderBy(target => target.TargetType)
            .ThenBy(target => target.Security.Symbol)
            .ToList();
    }

    private async Task<bool> HasFreshCacheAsync(string function, string cacheKey, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var entries = await db.ExternalApiCacheEntries.Where(e =>
            e.Provider == ProviderName &&
            e.Function == function &&
            e.CacheKey == cacheKey &&
            e.Succeeded)
            .ToListAsync(cancellationToken);
        return entries.Any(e => e.ExpiresAt > now);
    }

    private bool IsEnabled(out string message)
    {
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.ApiKey))
        {
            message = "Alpha Vantageデータ取得が無効、またはAPIキーが未設定です。";
            return false;
        }

        message = "";
        return true;
    }

    private static TimeSpan GetTtl(string function) => function switch
    {
        "GLOBAL_QUOTE" => DateTimeOffset.UtcNow.Date.AddDays(1) - DateTimeOffset.UtcNow,
        "TIME_SERIES_DAILY" => TimeSpan.FromDays(1),
        "CURRENCY_EXCHANGE_RATE" => TimeSpan.FromDays(1),
        "NEWS_SENTIMENT" => TimeSpan.FromHours(6),
        _ => TimeSpan.FromDays(7)
    };

    private static IReadOnlyList<DailyPriceBar> ParseDailyPrices(string payloadJson)
    {
        using var document = JsonDocument.Parse(payloadJson);
        if (!document.RootElement.TryGetProperty("Time Series (Daily)", out var series) || series.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return series.EnumerateObject()
            .Select(day => DateOnly.TryParse(day.Name, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) &&
                           TryGetDecimal(day.Value, "1. open", out var open) &&
                           TryGetDecimal(day.Value, "2. high", out var high) &&
                           TryGetDecimal(day.Value, "3. low", out var low) &&
                           TryGetDecimal(day.Value, "4. close", out var close) &&
                           TryGetLong(day.Value, "5. volume", out var volume)
                ? new DailyPriceBar(date, open, high, low, close, volume)
                : null)
            .Where(bar => bar is not null)
            .Cast<DailyPriceBar>()
            .OrderBy(bar => bar.Date)
            .ToList();
    }

    private static IReadOnlyList<NewsSentimentData> ParseNews(string payloadJson, string symbol)
    {
        using var document = JsonDocument.Parse(payloadJson);
        if (!document.RootElement.TryGetProperty("feed", out var feed) || feed.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<NewsSentimentData>();
        foreach (var item in feed.EnumerateArray())
        {
            if (!TryGetString(item, "title", out var title))
            {
                continue;
            }

            var sentiment = TryGetDecimal(item, "overall_sentiment_score", out var overall) ? overall : 0m;
            var relevance = 0.5m;
            if (item.TryGetProperty("ticker_sentiment", out var tickers) && tickers.ValueKind == JsonValueKind.Array)
            {
                foreach (var ticker in tickers.EnumerateArray())
                {
                    if (GetString(ticker, "ticker").Equals(symbol, StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryGetDecimal(ticker, "ticker_sentiment_score", out var tickerSentiment)) sentiment = tickerSentiment;
                        if (TryGetDecimal(ticker, "relevance_score", out var tickerRelevance)) relevance = tickerRelevance;
                    }
                }
            }

            items.Add(new NewsSentimentData(title, GetNullableString(item, "url"), ParseAlphaVantageTime(GetNullableString(item, "time_published")), sentiment, relevance, GetNullableString(item, "summary")));
        }

        return items;
    }

    private static DateTimeOffset? ParseAlphaVantageTime(string? value) =>
        DateTimeOffset.TryParseExact(value, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static decimal RankCandidate(SymbolCandidate candidate, Security security, string requestedSymbol)
    {
        var rank = candidate.MatchScore;
        if (candidate.Symbol.Equals(requestedSymbol, StringComparison.OrdinalIgnoreCase)) rank += 100m;
        else if (candidate.Symbol.StartsWith($"{requestedSymbol}.", StringComparison.OrdinalIgnoreCase)) rank += 80m;
        if (!string.IsNullOrWhiteSpace(security.Currency) && candidate.Currency.Equals(security.Currency, StringComparison.OrdinalIgnoreCase)) rank += 20m;
        if (!string.IsNullOrWhiteSpace(security.Country) && candidate.Region.Contains(security.Country, StringComparison.OrdinalIgnoreCase)) rank += 10m;
        if (candidate.Type.Equals("Equity", StringComparison.OrdinalIgnoreCase)) rank += 5m;
        return rank;
    }

    private static SymbolSearchResult ResolveSupportedStockSymbol(Security security, string normalizedSymbol)
    {
        if (IsJapaneseStockSymbol(security, normalizedSymbol))
        {
            return new SymbolSearchResult($"{normalizedSymbol}.T", "JPY", null);
        }

        if (IsUsStockSymbol(security, normalizedSymbol))
        {
            return new SymbolSearchResult(normalizedSymbol, "USD", null);
        }

        return new SymbolSearchResult(
            null,
            security.Currency,
            $"{normalizedSymbol} は対応対象外です。日本株は4桁コード、米国株は英字ティッカーのみ対応しています。");
    }

    private static bool IsJapaneseStockSymbol(Security security, string normalizedSymbol) =>
        normalizedSymbol.Length == 4 &&
        normalizedSymbol.All(char.IsDigit) &&
        (string.IsNullOrWhiteSpace(security.Country) || string.Equals(security.Country, "JP", StringComparison.OrdinalIgnoreCase)) &&
        (string.IsNullOrWhiteSpace(security.Currency) || string.Equals(security.Currency, "JPY", StringComparison.OrdinalIgnoreCase));

    private static bool IsUsStockSymbol(Security security, string normalizedSymbol) =>
        normalizedSymbol.Length is >= 1 and <= 10 &&
        normalizedSymbol.All(character => char.IsAsciiLetterUpper(character) || character == '.') &&
        (string.IsNullOrWhiteSpace(security.Country) || string.Equals(security.Country, "US", StringComparison.OrdinalIgnoreCase)) &&
        (string.IsNullOrWhiteSpace(security.Currency) || string.Equals(security.Currency, "USD", StringComparison.OrdinalIgnoreCase));

    private static string? GetAlphaVantageMessage(JsonElement payload)
    {
        if (TryGetMessage(payload, "Error Message", out var error)) return error;
        if (TryGetMessage(payload, "Information", out error)) return error;
        if (TryGetMessage(payload, "Note", out error)) return error;
        return "Alpha Vantageから利用できないレスポンスが返りました。";
    }

    private static bool TryGetDecimal(JsonElement parent, string propertyName, out decimal value)
    {
        value = 0m;
        if (!parent.TryGetProperty(propertyName, out var property)) return false;
        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetDecimal(out value),
            JsonValueKind.String => decimal.TryParse(property.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value),
            _ => false
        };
    }

    private static bool TryGetLong(JsonElement parent, string propertyName, out long value)
    {
        value = 0;
        if (!parent.TryGetProperty(propertyName, out var property)) return false;
        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt64(out value),
            JsonValueKind.String => long.TryParse(property.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value),
            _ => false
        };
    }

    private static bool TryGetMessage(JsonElement parent, string propertyName, out string message)
    {
        message = "";
        if (!parent.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String) return false;
        message = property.GetString() ?? "";
        return !string.IsNullOrWhiteSpace(message);
    }

    private static bool TryGetString(JsonElement parent, string propertyName, out string value)
    {
        value = "";
        if (!parent.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString() ?? "";
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string GetString(JsonElement parent, string propertyName) =>
        TryGetString(parent, propertyName, out var value) ? value : "";

    private static string? GetNullableString(JsonElement parent, string propertyName) =>
        TryGetString(parent, propertyName, out var value) ? value : null;

    private static decimal GetScore(JsonElement parent, string propertyName) =>
        TryGetString(parent, propertyName, out var value) &&
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var score)
            ? score
            : 0m;

    private sealed record CachedPayload(string PayloadJson, bool Succeeded, string? ErrorMessage);
    private sealed record SymbolSearchResult(string? Symbol, string? Currency, string? ErrorMessage);
    private sealed record SymbolCandidate(string Symbol, string Type, string Region, string Currency, decimal MatchScore);
    private sealed record PrefetchItem(string Function, Security Security);
    private sealed record CoverageTarget(Security Security, string TargetType);
}
