using Discord;
using Discord.WebSocket;
using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Infrastructure.Discord;
using InvestmentDecisionBot.Worker.Scheduling;
using Microsoft.Extensions.Options;

namespace InvestmentDecisionBot.Worker.HostedServices;

public sealed class DiscordBotHostedService(
    DiscordSocketClient client,
    IServiceProvider services,
    IOptions<DiscordOptions> options,
    ReportRunCoordinator coordinator,
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
                .WithType(ApplicationCommandOptionType.SubCommand));

        var report = new SlashCommandBuilder()
            .WithName("report")
            .WithDescription("投資判断レポートを生成します");

        var marketData = new SlashCommandBuilder()
            .WithName("marketdata")
            .WithDescription("Alpha Vantageデータ取得とキャッシュを管理します")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("status")
                .WithDescription("リクエスト残数と未取得キューを表示します")
                .WithType(ApplicationCommandOptionType.SubCommand))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("coverage")
                .WithDescription("監視対象銘柄のAlpha Vantageカバレッジを表示します")
                .WithType(ApplicationCommandOptionType.SubCommand))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("prefetch")
                .WithDescription("無料枠の範囲で未取得データを取得します")
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
                .WithTitle("Alpha Vantage取得状況")
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
                    item.IsAlphaVantageCovered ? "シンボル" : null,
                    item.HasFreshPrice ? "価格" : null,
                    item.HasDailySeries ? "日足" : null,
                    item.HasNewsSentiment ? "ニュース" : null,
                    item.HasExchangeRate ? "為替" : null
                }.Where(value => value is not null));
                var symbol = item.AlphaVantageSymbol is null ? item.Symbol : $"{item.Symbol}->{item.AlphaVantageSymbol}";
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
                .WithTitle("Alpha Vantageカバレッジ")
                .WithColor(Color.Teal)
                .AddField("監視対象", coverage.TargetCount, true)
                .AddField("シンボル解決済み", $"{coverage.AlphaVantageCoveredCount}/{coverage.TargetCount}", true)
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

        var limitValue = sub.Options.FirstOrDefault(option => option.Name == "limit")?.Value;
        var limit = limitValue is long longValue ? (int?)Math.Clamp(longValue, 0, int.MaxValue) : null;
        var result = await service.PrefetchAsync(limit, CancellationToken.None);
        var messages = result.Messages.Count == 0 ? "なし" : string.Join("\n", result.Messages.Select(message => $"- {message}"));
        var prefetchEmbed = new EmbedBuilder()
            .WithTitle("Alpha Vantage事前取得")
            .WithColor(result.Succeeded > 0 ? Color.Green : Color.Orange)
            .AddField("指定上限", result.RequestedLimit, true)
            .AddField("試行件数", result.Attempted, true)
            .AddField("成功件数", result.Succeeded, true)
            .AddField("スキップ件数", result.Skipped, true)
            .AddField("今日の使用数", result.UsedToday, true)
            .AddField("今日の残り", result.RemainingToday, true)
            .AddField("メッセージ", Truncate(messages, EmbedFieldLimit), false)
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

    private static string FormatWatchlistSource(object source) => source.ToString() switch
    {
        "Manual" => "手動追加",
        "SoldAutomatically" => "売却検出による自動追加",
        _ => source.ToString() ?? "不明"
    };

    private static string FormatTargetType(string targetType) => targetType switch
    {
        "Holding" => "保有",
        "Watchlist" => "監視",
        _ => targetType
    };
}
