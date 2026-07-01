using Discord;
using Discord.WebSocket;
using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Application.DTOs;
using InvestmentDecisionBot.Presentation.Discord.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvestmentDecisionBot.Presentation.Discord.HostedServices;

public sealed class DiscordBotHostedService(
    DiscordSocketClient client,
    IServiceProvider services,
    IOptions<DiscordOptions> options,
    IReportRunCoordinator coordinator,
    ILogger<DiscordBotHostedService> logger) : IHostedService
{
    private const int EmbedDescriptionLimit = 4000;
    private const int EmbedFieldLimit = 1024;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.Token))
        {
            logger.LogWarning("DISCORD_TOKEN is not configured. Discord bot will not connect.");
            return;
        }

        client.Ready += OnReadyAsync;
        client.SlashCommandExecuted += OnSlashCommandExecutedAsync;
        await client.LoginAsync(TokenType.Bot, options.Value.Token);
        await client.StartAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (client.LoginState == LoginState.LoggedIn)
        {
            await client.StopAsync();
            await client.LogoutAsync();
        }
    }

    private async Task OnReadyAsync()
    {
        if (options.Value.GuildId == 0)
        {
            logger.LogWarning("DISCORD_GUILD_ID is not configured. Slash commands were not registered.");
            return;
        }

        var guild = client.GetGuild(options.Value.GuildId);
        if (guild is null)
        {
            logger.LogWarning("Discord guild {GuildId} was not found.", options.Value.GuildId);
            return;
        }

        var import = new SlashCommandBuilder()
            .WithName("import")
            .WithDescription("SBI証券CSVを取り込みます")
            .AddOption("file", ApplicationCommandOptionType.Attachment, "SBI証券CSVファイル", isRequired: true);

        var watch = new SlashCommandBuilder()
            .WithName("watch")
            .WithDescription("ウォッチリストを管理します")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("add")
                .WithDescription("銘柄をウォッチリストに追加します")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("symbol", ApplicationCommandOptionType.String, "銘柄コード", isRequired: true))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("remove")
                .WithDescription("銘柄をウォッチリストから外します")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("symbol", ApplicationCommandOptionType.String, "銘柄コード", isRequired: true))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("list")
                .WithDescription("ウォッチリストを表示します")
                .WithType(ApplicationCommandOptionType.SubCommand))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("targets")
                .WithDescription("監視対象に入っている銘柄情報を表示します")
                .WithType(ApplicationCommandOptionType.SubCommand));

        var report = new SlashCommandBuilder()
            .WithName("report")
            .WithDescription("投資判断レポートを生成します");

        var marketData = new SlashCommandBuilder()
            .WithName("marketdata")
            .WithDescription("市場データ取得とキャッシュを管理します")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("status")
                .WithDescription("リクエスト残数と未取得キューを表示します")
                .WithType(ApplicationCommandOptionType.SubCommand))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("coverage")
                .WithDescription("監視対象銘柄の市場データカバレッジを表示します")
                .WithType(ApplicationCommandOptionType.SubCommand))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("data")
                .WithDescription("APIから取得済みのデータを銘柄別に表示します")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("symbol", ApplicationCommandOptionType.String, "4桁の銘柄コード", isRequired: true))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("articles")
                .WithDescription("APIから取得済みの記事データを銘柄別に表示します")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("symbol", ApplicationCommandOptionType.String, "4桁の銘柄コード", isRequired: true)
                .AddOption("limit", ApplicationCommandOptionType.Integer, "表示する記事数", isRequired: false))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("prefetch")
                .WithDescription("設定上限の範囲で未取得データを取得します")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("limit", ApplicationCommandOptionType.Integer, "最大取得リクエスト数", isRequired: false));

        await guild.BulkOverwriteApplicationCommandAsync([import.Build(), watch.Build(), report.Build(), marketData.Build()]);
        logger.LogInformation("Slash commands registered.");
    }

    private async Task OnSlashCommandExecutedAsync(SocketSlashCommand command)
    {
        if (options.Value.ChannelId != 0 && command.ChannelId != options.Value.ChannelId)
        {
            await command.RespondAsync(embed: BuildEmbed("利用できないチャンネルです", "このBotは設定済みのDiscordチャンネルでのみ利用できます。", Color.Orange), ephemeral: true);
            return;
        }

        try
        {
            using var scope = services.CreateScope();
            switch (command.CommandName)
            {
                case "import":
                    await HandleImportAsync(scope.ServiceProvider, command);
                    break;
                case "watch":
                    await HandleWatchAsync(scope.ServiceProvider, command);
                    break;
                case "report":
                    await HandleReportAsync(scope.ServiceProvider, command);
                    break;
                case "marketdata":
                    await HandleMarketDataAsync(scope.ServiceProvider, command);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Slash command failed.");
            var embed = BuildEmbed("コマンド処理に失敗しました", ex.Message, Color.Red);
            if (!command.HasResponded)
            {
                await command.RespondAsync(embed: embed, ephemeral: true);
            }
            else
            {
                await command.FollowupAsync(embed: embed, ephemeral: true);
            }
        }
    }

    private static async Task HandleImportAsync(IServiceProvider provider, SocketSlashCommand command)
    {
        await command.DeferAsync();
        var attachment = command.Data.Options.FirstOrDefault(o => o.Name == "file")?.Value as Attachment;
        if (attachment is null)
        {
            await command.FollowupAsync(embed: BuildEmbed("CSVファイルがありません", "SBI証券CSVファイルを添付してください。", Color.Orange));
            return;
        }

        using var http = new HttpClient();
        await using var stream = await http.GetStreamAsync(attachment.Url);
        var imports = provider.GetRequiredService<IImportService>();
        var result = await imports.ImportSbiCsvAsync(stream, attachment.Filename, CancellationToken.None);
        var embed = new EmbedBuilder()
            .WithTitle(result.Succeeded ? "CSV取り込み完了" : "CSV取り込み失敗")
            .WithColor(result.Succeeded ? Color.Green : Color.Red)
            .WithDescription(Truncate(result.Message, EmbedDescriptionLimit))
            .AddField("取り込み件数", result.ImportedCount, true)
            .AddField("新規作成", result.CreatedCount, true)
            .AddField("更新", result.UpdatedCount, true)
            .AddField("売却済み検出", result.SoldDetectedCount, true)
            .AddField("ウォッチリスト追加", result.WatchlistAddedCount, true)
            .AddField("文字コード", string.IsNullOrWhiteSpace(result.EncodingName) ? "不明" : result.EncodingName, true)
            .WithCurrentTimestamp()
            .Build();

        await command.FollowupAsync(embed: embed);
    }

    private static async Task HandleWatchAsync(IServiceProvider provider, SocketSlashCommand command)
    {
        var sub = command.Data.Options.First();
        var service = provider.GetRequiredService<IWatchlistService>();
        if (sub.Name == "list")
        {
            var items = await service.ListAsync(CancellationToken.None);
            var description = items.Count == 0
                ? "ウォッチリストは空です。"
                : string.Join("\n", items.Select(i => $"- `{i.Symbol}` {i.Name} / {FormatWatchlistSource(i.Source)}{(i.IsHolding ? " / 保有中" : "")}"));
            var embed = new EmbedBuilder()
                .WithTitle("ウォッチリスト")
                .WithDescription(Truncate(description, EmbedDescriptionLimit))
                .WithColor(Color.Blue)
                .AddField("登録数", items.Count, true)
                .WithCurrentTimestamp()
                .Build();
            await command.RespondAsync(embed: embed);
            return;
        }

        if (sub.Name == "targets")
        {
            var targets = await service.ListTargetsAsync(CancellationToken.None);
            var embed = new EmbedBuilder()
                .WithTitle("監視対象銘柄")
                .WithDescription(Truncate(FormatWatchTargets(targets), EmbedDescriptionLimit))
                .WithColor(Color.Blue)
                .AddField("対象数", targets.Count, true)
                .AddField("保有", targets.Count(target => target.IsHolding), true)
                .AddField("ウォッチ", targets.Count(target => target.IsWatchlisted), true)
                .WithCurrentTimestamp()
                .Build();
            await command.RespondAsync(embed: embed);
            return;
        }

        var symbol = sub.Options.First(o => o.Name == "symbol").Value.ToString() ?? "";
        var result = sub.Name == "add"
            ? await service.AddAsync(symbol, CancellationToken.None)
            : await service.RemoveAsync(symbol, CancellationToken.None);
        var title = sub.Name == "add" ? "ウォッチリスト追加" : "ウォッチリスト削除";
        await command.RespondAsync(embed: BuildEmbed(title, result.Message, result.Succeeded ? Color.Green : Color.Orange));
    }

    private async Task HandleReportAsync(IServiceProvider provider, SocketSlashCommand command)
    {
        await command.DeferAsync();
        var service = provider.GetRequiredService<IReportService>();
        var ran = await coordinator.TryRunAsync(async ct =>
        {
            var result = await service.GenerateDailyReportAsync(postToDiscord: false, ct);
            var embed = new EmbedBuilder()
                .WithTitle("投資判断レポート")
                .WithDescription(Truncate(result.Content, EmbedDescriptionLimit))
                .WithColor(result.Succeeded ? Color.Blue : Color.Red)
                .AddField("分析対象数", result.AnalysisCount, true)
                .AddField("Discord投稿", result.DiscordMessageId is null ? "未投稿" : result.DiscordMessageId, true)
                .WithCurrentTimestamp()
                .Build();
            await command.FollowupAsync(embed: embed);
        }, CancellationToken.None);

        if (!ran)
        {
            await command.FollowupAsync(embed: BuildEmbed("レポート生成中", "レポート生成はすでに実行中です。", Color.Orange));
        }
    }

    private static async Task HandleMarketDataAsync(IServiceProvider provider, SocketSlashCommand command)
    {
        await command.DeferAsync(ephemeral: true);
        var sub = command.Data.Options.First();
        var service = provider.GetRequiredService<IMarketDataPrefetchService>();
        if (sub.Name == "status")
        {
            var status = await service.GetStatusAsync(CancellationToken.None);
            var next = status.NextItems.Count == 0 ? "なし" : string.Join(", ", status.NextItems);
            var embed = new EmbedBuilder()
                .WithTitle("市場データ取得状況")
                .WithColor(Color.Teal)
                .AddField("1日の上限", status.DailyLimit, true)
                .AddField("今日の使用数", status.UsedToday, true)
                .AddField("今日の残り", status.RemainingToday, true)
                .AddField("未取得キュー", status.PendingCount, true)
                .AddField("次の取得候補", Truncate(next, EmbedFieldLimit), false)
                .WithCurrentTimestamp()
                .Build();
            await UpdateDeferredResponseAsync(command, embed);
            return;
        }

        if (sub.Name == "coverage")
        {
            var coverage = await service.GetCoverageAsync(CancellationToken.None);
            var lines = coverage.Items.Take(20).Select(item =>
            {
                var data = string.Join(", ", new[]
                {
                    item.IsProviderCovered ? "外部シンボル" : null,
                    item.HasFreshPrice ? "価格" : null,
                    item.HasDailySeries ? "日足" : null,
                    item.HasNewsSentiment ? "ニュース" : null,
                    item.HasExchangeRate ? "為替" : null
                }.Where(value => value is not null));
                var symbol = item.ExternalSymbol is null ? item.Symbol : $"{item.Symbol}->{item.ExternalSymbol}";
                var error = string.IsNullOrWhiteSpace(item.ResolutionError) ? "" : $" / エラー: {item.ResolutionError}";
                return $"- `{symbol}` {FormatTargetType(item.TargetType)}: {(string.IsNullOrWhiteSpace(data) ? "なし" : data)}{error}";
            });
            var detail = coverage.Items.Count == 0
                ? "なし"
                : string.Join("\n", lines);
            if (coverage.Items.Count > 20)
            {
                detail += $"\n- 他 {coverage.Items.Count - 20} 件";
            }

            var embed = new EmbedBuilder()
                .WithTitle("市場データカバレッジ")
                .WithColor(Color.Teal)
                .AddField("監視対象", coverage.TargetCount, true)
                .AddField("外部シンボル解決済み", $"{coverage.ProviderCoveredCount}/{coverage.TargetCount}", true)
                .AddField("価格キャッシュあり", $"{coverage.PriceCachedCount}/{coverage.TargetCount}", true)
                .AddField("日足キャッシュあり", $"{coverage.DailyCachedCount}/{coverage.TargetCount}", true)
                .AddField("ニュースキャッシュあり", $"{coverage.NewsCachedCount}/{coverage.TargetCount}", true)
                .AddField("為替カバー済み", $"{coverage.ExchangeRateCachedCount}/{coverage.TargetCount}", true)
                .AddField("銘柄別", Truncate(detail, EmbedFieldLimit), false)
                .WithCurrentTimestamp()
                .Build();
            await UpdateDeferredResponseAsync(command, embed);
            return;
        }

        if (sub.Name == "data")
        {
            var symbol = sub.Options.First(option => option.Name == "symbol").Value.ToString() ?? "";
            var detail = await service.GetDetailAsync(symbol, CancellationToken.None);
            if (!detail.Found)
            {
                await UpdateDeferredResponseAsync(command, BuildEmbed("API取得データ", detail.Message ?? "銘柄が見つかりません。", Color.Orange));
                return;
            }

            var latestPrices = detail.DailyPrices.Count == 0
                ? "なし"
                : string.Join("\n", detail.DailyPrices
                    .OrderByDescending(price => price.Date)
                    .Take(5)
                    .Select(price => $"- `{price.Date:yyyy-MM-dd}` 終値 {price.Close:N2} / 出来高 {price.Volume:N0}"));
            var embed = new EmbedBuilder()
                .WithTitle($"API取得データ: {detail.Symbol} {detail.Name}")
                .WithColor(Color.Teal)
                .AddField("日足", Truncate(latestPrices, EmbedFieldLimit), false)
                .AddField("財務", Truncate(FormatFinancialSnapshot(detail.FinancialSnapshot, detail.Message), EmbedFieldLimit), false)
                .AddField("ニュース", Truncate(FormatNews(detail.News), EmbedFieldLimit), false)
                .AddField("APIキャッシュ", Truncate(FormatCacheEntries(detail.CacheEntries), EmbedFieldLimit), false)
                .WithCurrentTimestamp()
                .Build();
            await UpdateDeferredResponseAsync(command, embed);
            return;
        }

        if (sub.Name == "articles")
        {
            var symbol = sub.Options.First(option => option.Name == "symbol").Value.ToString() ?? "";
            var articleLimitValue = sub.Options.FirstOrDefault(option => option.Name == "limit")?.Value;
            var articleLimit = articleLimitValue is long articleLongValue ? (int)Math.Clamp(articleLongValue, 1, 20) : 10;
            var detail = await service.GetDetailAsync(symbol, CancellationToken.None);
            if (!detail.Found)
            {
                await UpdateDeferredResponseAsync(command, BuildEmbed("API取得記事", detail.Message ?? "銘柄が見つかりません。", Color.Orange));
                return;
            }

            var articleCount = detail.News.Count;
            var embed = new EmbedBuilder()
                .WithTitle($"API取得記事: {detail.Symbol} {detail.Name}")
                .WithColor(articleCount > 0 ? Color.Teal : Color.Orange)
                .AddField("記事数", articleCount, true)
                .AddField("表示件数", Math.Min(articleCount, articleLimit), true)
                .AddField("記事", Truncate(FormatArticles(detail.News, articleLimit), EmbedFieldLimit), false)
                .WithCurrentTimestamp()
                .Build();
            await UpdateDeferredResponseAsync(command, embed);
            return;
        }

        var limitValue = sub.Options.FirstOrDefault(option => option.Name == "limit")?.Value;
        var limit = limitValue is long longValue ? (int?)Math.Clamp(longValue, 0, int.MaxValue) : null;
        var result = await service.PrefetchAsync(limit, CancellationToken.None);
        var messages = result.Messages.Count == 0 ? "なし" : string.Join("\n", result.Messages.Select(message => $"- {message}"));
        var requestLogs = FormatRequestLogs(result.RequestLogs);
        var prefetchEmbed = new EmbedBuilder()
            .WithTitle("市場データ事前取得")
            .WithColor(result.Succeeded > 0 ? Color.Green : Color.Orange)
            .AddField("指定上限", result.RequestedLimit, true)
            .AddField("試行件数", result.Attempted, true)
            .AddField("成功件数", result.Succeeded, true)
            .AddField("スキップ件数", result.Skipped, true)
            .AddField("今日の使用数", result.UsedToday, true)
            .AddField("今日の残り", result.RemainingToday, true)
            .AddField("メッセージ", Truncate(messages, EmbedFieldLimit), false)
            .AddField("リクエストログ", Truncate(requestLogs, EmbedFieldLimit), false)
            .WithCurrentTimestamp()
            .Build();
        await UpdateDeferredResponseAsync(command, prefetchEmbed);
    }

    private static Embed BuildEmbed(string title, string description, Color color) =>
        new EmbedBuilder()
            .WithTitle(title)
            .WithDescription(Truncate(description, EmbedDescriptionLimit))
            .WithColor(color)
            .WithCurrentTimestamp()
            .Build();

    private static Task UpdateDeferredResponseAsync(SocketSlashCommand command, Embed embed) =>
        command.ModifyOriginalResponseAsync(message =>
        {
            message.Content = "";
            message.Embed = embed;
        });

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "なし";
        }

        return value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 20)] + "\n...（省略）";
    }

    private static string FormatRequestLogs(IReadOnlyList<MarketDataRequestLogItem> requestLogs)
    {
        if (requestLogs.Count == 0)
        {
            return "なし";
        }

        var lines = requestLogs
            .Take(10)
            .Select(log =>
            {
                var status = log.Succeeded ? "OK" : "NG";
                var error = string.IsNullOrWhiteSpace(log.ErrorMessage) ? "" : $" / {log.ErrorMessage}";
                return $"- `{log.RequestedAt:HH:mm:ss}` {status} {log.Function}:{log.CacheKey}{error}";
            })
            .ToList();

        if (requestLogs.Count > 10)
        {
            lines.Add($"- ほか {requestLogs.Count - 10} 件");
        }

        return string.Join("\n", lines);
    }

    private static string FormatFinancialSnapshot(FinancialSnapshotData? snapshot, string? message)
    {
        if (snapshot is null)
        {
            return string.IsNullOrWhiteSpace(message) ? "なし" : message;
        }

        return string.Join("\n", new[]
        {
            snapshot.DisclosureDate is null ? null : $"- 開示日: `{snapshot.DisclosureDate:yyyy-MM-dd}`",
            snapshot.NetSales is null ? null : $"- 売上高: {snapshot.NetSales:N0}",
            snapshot.OperatingProfit is null ? null : $"- 営業利益: {snapshot.OperatingProfit:N0}",
            snapshot.Profit is null ? null : $"- 純利益: {snapshot.Profit:N0}",
            snapshot.Eps is null ? null : $"- EPS: {snapshot.Eps:N2}",
            snapshot.EquityRatio is null ? null : $"- 自己資本比率: {snapshot.EquityRatio:P1}"
        }.Where(line => line is not null));
    }

    private static string FormatNews(IReadOnlyList<NewsSentimentData> news)
    {
        if (news.Count == 0)
        {
            return "なし";
        }

        return string.Join("\n", news
            .Take(5)
            .Select(item =>
            {
                var published = item.PublishedAt is null ? "" : $" `{item.PublishedAt:yyyy-MM-dd}`";
                var score = item.SentimentScore > 0 ? "Positive" : item.SentimentScore < 0 ? "Negative" : "Neutral";
                return $"-{published} {score}: {item.Title}";
            }));
    }

    private static string FormatArticles(IReadOnlyList<NewsSentimentData> news, int limit)
    {
        if (news.Count == 0)
        {
            return "取得済みの記事はありません。`/marketdata prefetch` を実行するとGDELTから記事取得を試みます。";
        }

        var lines = news
            .OrderByDescending(item => item.PublishedAt ?? item.FetchedAt)
            .Take(limit)
            .Select(item =>
            {
                var published = item.PublishedAt is null ? "日付不明" : item.PublishedAt.Value.ToString("yyyy-MM-dd HH:mm");
                var source = string.IsNullOrWhiteSpace(item.Source) ? "source unknown" : item.Source;
                var sentiment = item.SentimentScore > 0 ? "Positive" : item.SentimentScore < 0 ? "Negative" : "Neutral";
                var title = string.IsNullOrWhiteSpace(item.Url) ? item.Title : $"[{item.Title}]({item.Url})";
                var summary = string.IsNullOrWhiteSpace(item.Summary) ? "" : $"\n  {item.Summary}";
                return $"- `{published}` {source} / {sentiment} / relevance {item.RelevanceScore:P0}\n  {title}{summary}";
            })
            .ToList();

        if (news.Count > limit)
        {
            lines.Add($"- 他 {news.Count - limit} 件");
        }

        return string.Join("\n", lines);
    }

    private static string FormatCacheEntries(IReadOnlyList<ExternalApiCacheSummary> entries)
    {
        if (entries.Count == 0)
        {
            return "なし";
        }

        var lines = entries
            .Take(8)
            .Select(entry =>
            {
                var status = entry.Succeeded ? "OK" : "NG";
                var error = string.IsNullOrWhiteSpace(entry.ErrorMessage) ? "" : $" / {entry.ErrorMessage}";
                return $"- `{entry.FetchedAt:yyyy-MM-dd HH:mm}` {status} {entry.Provider}:{entry.Function} ({entry.PayloadLength:N0} bytes){error}";
            })
            .ToList();

        if (entries.Count > 8)
        {
            lines.Add($"- 他 {entries.Count - 8} 件");
        }

        return string.Join("\n", lines);
    }

    private static string FormatWatchTargets(IReadOnlyList<WatchTargetDto> targets)
    {
        if (targets.Count == 0)
        {
            return "監視対象銘柄はありません。`/watch add` またはSBI CSV取り込みで追加できます。";
        }

        var lines = targets
            .Take(25)
            .Select(target =>
            {
                var targetType = string.Join("+", new[]
                {
                    target.IsHolding ? "保有" : null,
                    target.IsWatchlisted ? "監視" : null
                }.Where(value => value is not null));
                var market = string.Join("/", new[] { target.Market, target.Country, target.Currency }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
                var external = string.IsNullOrWhiteSpace(target.ExternalSymbol) ? "" : $" / 外部: {target.ExternalSymbol}";
                var source = target.WatchlistSource is null ? "" : $" / 登録元: {FormatWatchlistSource(target.WatchlistSource)}";
                var error = string.IsNullOrWhiteSpace(target.ExternalSymbolResolutionError) ? "" : $" / エラー: {target.ExternalSymbolResolutionError}";
                return $"- `{target.Symbol}` {target.Name} / {FormatSecurityType(target.SecurityType)} / {targetType}{(string.IsNullOrWhiteSpace(market) ? "" : $" / {market}")}{external}{source}{error}";
            })
            .ToList();

        if (targets.Count > 25)
        {
            lines.Add($"- 他 {targets.Count - 25} 件");
        }

        return string.Join("\n", lines);
    }

    private static string FormatWatchlistSource(object source) => source.ToString() switch
    {
        "Manual" => "手動追加",
        "SoldAutomatically" => "売却検出による自動追加",
        _ => source.ToString() ?? "不明"
    };

    private static string FormatSecurityType(string securityType) => securityType switch
    {
        "Stock" => "株式",
        _ => securityType
    };

    private static string FormatTargetType(string targetType) => targetType switch
    {
        "Holding" => "保有",
        "Watchlist" => "監視",
        _ => targetType
    };
}
