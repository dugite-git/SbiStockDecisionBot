using Discord;

namespace InvestmentDecisionBot.Presentation.Discord.Ui;

public static class DiscordComponentFactory
{
    public static MessageComponent ImportNextActions() =>
        new ComponentBuilder()
            .WithButton("監視対象を見る", "watch:targets:page:0", ButtonStyle.Secondary)
            .WithButton("市場データ取得", "hint:marketdata:prefetch", ButtonStyle.Primary)
            .WithButton("レポート生成", "hint:report", ButtonStyle.Primary)
            .Build();

    public static MessageComponent WatchActions(string symbol) =>
        new ComponentBuilder()
            .WithButton("市場データを見る", $"hint:marketdata:data:{symbol}", ButtonStyle.Secondary)
            .WithButton("記事を見る", $"hint:marketdata:articles:{symbol}", ButtonStyle.Secondary)
            .WithButton("監視対象一覧", "watch:targets:page:0", ButtonStyle.Secondary)
            .Build();

    public static MessageComponent WatchList(int page, int totalPages) =>
        new ComponentBuilder()
            .WithButton("前へ", $"watch:list:page:{Math.Max(0, page - 1)}", ButtonStyle.Secondary, disabled: page <= 0)
            .WithButton("次へ", $"watch:list:page:{page + 1}", ButtonStyle.Secondary, disabled: page >= totalPages - 1)
            .WithButton("監視対象を見る", "watch:targets:page:0", ButtonStyle.Primary)
            .Build();

    public static MessageComponent WatchTargets(int page, int totalPages, bool unresolvedOnly) =>
        new ComponentBuilder()
            .WithButton("前へ", $"watch:targets:{FilterSegment(unresolvedOnly)}:page:{Math.Max(0, page - 1)}", ButtonStyle.Secondary, disabled: page <= 0)
            .WithButton("次へ", $"watch:targets:{FilterSegment(unresolvedOnly)}:page:{page + 1}", ButtonStyle.Secondary, disabled: page >= totalPages - 1)
            .WithButton(unresolvedOnly ? "全件表示" : "未解決のみ", $"watch:targets:{FilterSegment(!unresolvedOnly)}:page:0", ButtonStyle.Secondary)
            .WithButton("市場データ取得", "hint:marketdata:prefetch", ButtonStyle.Primary)
            .Build();

    public static MessageComponent MarketDataStatus() =>
        new ComponentBuilder()
            .WithButton("事前取得する", "hint:marketdata:prefetch", ButtonStyle.Primary)
            .WithButton("カバレッジを見る", "marketdata:coverage:all:page:0", ButtonStyle.Secondary)
            .Build();

    public static MessageComponent MarketDataCoverage(int page, int totalPages, bool missingOnly) =>
        new ComponentBuilder()
            .WithButton("前へ", $"marketdata:coverage:{CoverageFilterSegment(missingOnly)}:page:{Math.Max(0, page - 1)}", ButtonStyle.Secondary, disabled: page <= 0)
            .WithButton("次へ", $"marketdata:coverage:{CoverageFilterSegment(missingOnly)}:page:{page + 1}", ButtonStyle.Secondary, disabled: page >= totalPages - 1)
            .WithButton(missingOnly ? "全件表示" : "不足のみ", $"marketdata:coverage:{CoverageFilterSegment(!missingOnly)}:page:0", ButtonStyle.Secondary)
            .WithButton("事前取得", "hint:marketdata:prefetch", ButtonStyle.Primary)
            .Build();

    public static MessageComponent MarketDataArticles(string symbol, int page, int totalPages, int limit) =>
        new ComponentBuilder()
            .WithButton("前へ", $"marketdata:articles:{symbol}:limit:{limit}:page:{Math.Max(0, page - 1)}", ButtonStyle.Secondary, disabled: page <= 0)
            .WithButton("次へ", $"marketdata:articles:{symbol}:limit:{limit}:page:{page + 1}", ButtonStyle.Secondary, disabled: page >= totalPages - 1)
            .WithButton("事前取得", "hint:marketdata:prefetch", ButtonStyle.Primary)
            .Build();

    public static MessageComponent MarketDataDetail(string symbol) =>
        new ComponentBuilder()
            .WithButton("記事を見る", $"marketdata:articles:{symbol}:limit:10:page:0", ButtonStyle.Secondary)
            .WithButton("再取得", "hint:marketdata:prefetch", ButtonStyle.Secondary)
            .WithButton("レポート生成", "hint:report", ButtonStyle.Primary)
            .Build();

    public static MessageComponent PrefetchResult() =>
        new ComponentBuilder()
            .WithButton("カバレッジを見る", "marketdata:coverage:all:page:0", ButtonStyle.Secondary)
            .WithButton("レポート生成", "hint:report", ButtonStyle.Primary)
            .Build();

    public static MessageComponent ReportActions() =>
        new ComponentBuilder()
            .WithButton("Markdown出力済み", "hint:report:markdown", ButtonStyle.Secondary, disabled: true)
            .WithButton("保有銘柄", "watch:targets:page:0", ButtonStyle.Secondary)
            .WithButton("市場データ", "marketdata:coverage:all:page:0", ButtonStyle.Secondary)
            .Build();

    public static MessageComponent HintOnly() =>
        new ComponentBuilder()
            .WithButton("OK", "hint:noop", ButtonStyle.Secondary, disabled: true)
            .Build();

    private static string FilterSegment(bool unresolvedOnly) => unresolvedOnly ? "unresolved" : "all";

    private static string CoverageFilterSegment(bool missingOnly) => missingOnly ? "missing" : "all";
}
