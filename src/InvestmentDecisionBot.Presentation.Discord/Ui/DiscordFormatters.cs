using System.Text;
using InvestmentDecisionBot.Application.DTOs;
using InvestmentDecisionBot.Domain.Enums;

namespace InvestmentDecisionBot.Presentation.Discord.Ui;

public static class DiscordFormatters
{
    public const int EmbedDescriptionLimit = 4000;
    public const int EmbedFieldLimit = 1024;
    public const int WatchPageSize = 10;
    public const int CoveragePageSize = 10;
    public const int ArticlePageSize = 5;

    private static readonly TimeZoneInfo TokyoTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");

    public static string FormatJst(DateTimeOffset? value)
    {
        if (value is null)
        {
            return "なし";
        }

        var jst = TimeZoneInfo.ConvertTime(value.Value, TokyoTimeZone);
        return jst.ToString("yyyy-MM-dd HH:mm 'JST'");
    }

    public static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "なし";
        }

        return value.Length <= maxLength
            ? value
            : value[..Math.Max(0, maxLength - 20)] + "\n...（省略）";
    }

    public static int PageCount(int count, int pageSize) =>
        Math.Max(1, (int)Math.Ceiling(count / (double)pageSize));

    public static string PageLabel(int page, int totalPages) =>
        $"{Math.Min(page + 1, totalPages)}/{totalPages}";

    public static string FormatWatchlistSource(WatchlistSource source) => source switch
    {
        WatchlistSource.Manual => "手動追加",
        WatchlistSource.SoldAutomatically => "売却検出",
        _ => source.ToString()
    };

    public static string FormatTargetType(string targetType) => targetType switch
    {
        "Holding" => "保有",
        "Watchlist" => "監視",
        _ => targetType
    };

    public static string FormatSecurityType(string securityType) => securityType switch
    {
        "Stock" => "株式",
        _ => securityType
    };

    public static string FormatDecision(BotDecision decision) => decision switch
    {
        BotDecision.TakeProfit => "利確候補",
        BotDecision.PartialTakeProfit => "一部利確候補",
        BotDecision.StopLoss => "損切り候補",
        BotDecision.PartialStopLoss => "一部損切り候補",
        BotDecision.NewBuy => "新規買い候補",
        BotDecision.Hold => "保留",
        _ => decision.ToString()
    };

    public static string FormatSubScores(ReportDecisionSummaryItem item) =>
        $"F {item.FundamentalScore:0.##} | Q {item.QualityScore:0.##} | M {item.MomentumScore:0.##} | N {item.NewsScore:0.##} | R {item.PositionRiskScore:0.##}";

    public static string FormatRate(decimal? rate) =>
        rate is null ? "N/A" : $"{rate:+0.##;-0.##;0}%";

    public static string FormatPercent(decimal value) =>
        $"{value:P0}";

    public static string CoverageIcon(bool value) => value ? "✅" : "❌";

    public static bool HasCoverageMissing(MarketDataCoverageItem item) =>
        !item.IsProviderCovered || !item.HasFreshPrice || !item.HasDailySeries || !item.HasNewsSentiment || !item.HasExchangeRate || !string.IsNullOrWhiteSpace(item.ResolutionError);

    public static string FormatArticles(IReadOnlyList<NewsSentimentData> news, int page, int pageSize)
    {
        if (news.Count == 0)
        {
            return "取得済みの記事はありません。`/marketdata prefetch` を実行するとGDELTから記事取得を試みます。";
        }

        var lines = news
            .OrderByDescending(item => item.RelevanceScore)
            .ThenByDescending(item => item.PublishedAt ?? item.FetchedAt)
            .Skip(page * pageSize)
            .Take(pageSize)
            .Select((item, index) =>
            {
                var published = item.PublishedAt is null ? "日付不明" : FormatJst(item.PublishedAt);
                var source = string.IsNullOrWhiteSpace(item.Source) ? "source unknown" : item.Source;
                var sentiment = item.SentimentScore > 0 ? "Positive" : item.SentimentScore < 0 ? "Negative" : "Neutral";
                var title = string.IsNullOrWhiteSpace(item.Url) ? item.Title : $"[{item.Title}]({item.Url})";
                var summary = string.IsNullOrWhiteSpace(item.Summary) ? "" : $"\n  {Truncate(item.Summary, 180)}";
                return $"{page * pageSize + index + 1}. {published} / {source} / {sentiment} / relevance {item.RelevanceScore:P0}\n{title}{summary}";
            });

        return string.Join("\n\n", lines);
    }

    public static string FormatRequestLogs(IReadOnlyList<MarketDataRequestLogItem> requestLogs)
    {
        if (requestLogs.Count == 0)
        {
            return "なし";
        }

        var ordered = requestLogs
            .OrderBy(log => log.Succeeded)
            .ThenByDescending(log => log.RequestedAt)
            .Take(10)
            .Select(log =>
            {
                var status = log.Succeeded ? "OK" : "NG";
                var error = string.IsNullOrWhiteSpace(log.ErrorMessage) ? "" : $" / {log.ErrorMessage}";
                return $"- `{FormatJst(log.RequestedAt)}` {status} {log.Function}:{log.CacheKey}{error}";
            })
            .ToList();

        if (requestLogs.Count > 10)
        {
            ordered.Add($"- 他 {requestLogs.Count - 10} 件");
        }

        return string.Join("\n", ordered);
    }

    public static string BuildCommandHint(string command) =>
        $"このボタンは誤操作防止のため直接実行しません。次に `{command}` を実行してください。";

    public static string JoinNonEmpty(params string?[] values) =>
        string.Join("\n", values.Where(value => !string.IsNullOrWhiteSpace(value)));

    public static string BuildReportSummary(ReportResult result)
    {
        var counts = result.DecisionCounts ?? new ReportDecisionCounts(0, 0, 0, 0, 0, 0, 0);
        var lines = new StringBuilder();
        lines.AppendLine($"分析対象: {result.AnalysisCount}件");
        lines.AppendLine($"重要判断: {counts.ImportantTotal}件");
        lines.AppendLine($"利確候補: {counts.TakeProfit}件 / 一部利確候補: {counts.PartialTakeProfit}件");
        lines.AppendLine($"損切り候補: {counts.StopLoss}件 / 一部損切り候補: {counts.PartialStopLoss}件");
        lines.AppendLine($"新規買い候補: {counts.NewBuy}件 / 保留: {counts.Hold}件");

        var important = result.ImportantItems ?? Array.Empty<ReportDecisionSummaryItem>();
        if (important.Count == 0)
        {
            lines.AppendLine();
            lines.AppendLine("重要な判断はありません。");
        }
        else
        {
            lines.AppendLine();
            lines.AppendLine("重要な判断");
            foreach (var item in important.Take(5))
            {
                lines.AppendLine();
                lines.AppendLine($"`{item.Symbol}` {item.Name}");
                lines.AppendLine($"判断: {FormatDecision(item.Decision)}");
                lines.AppendLine($"総合スコア: {item.TotalScore:0.##}");
                lines.AppendLine($"含み損益率: {FormatRate(item.UnrealizedProfitLossRate)}");
                lines.AppendLine($"信頼度: {item.Confidence:P0}");
                lines.AppendLine($"SubScores: {FormatSubScores(item)}");
                lines.AppendLine($"理由: {Truncate(item.Reason, 180)}");
            }
        }

        var missing = result.MissingDataCategories ?? Array.Empty<string>();
        if (missing.Count > 0)
        {
            lines.AppendLine();
            lines.AppendLine($"不足データ: {string.Join(", ", missing)}");
        }

        return lines.ToString().TrimEnd();
    }
}
