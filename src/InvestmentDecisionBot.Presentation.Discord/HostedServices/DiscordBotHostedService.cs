using System.Text;
using Discord;
using Discord.WebSocket;
using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Application.DTOs;
using InvestmentDecisionBot.Domain.Enums;
using InvestmentDecisionBot.Presentation.Discord.Options;
using InvestmentDecisionBot.Presentation.Discord.Ui;
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
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.Token))
        {
            logger.LogWarning("DISCORD_TOKEN is not configured. Discord bot will not connect.");
            return;
        }

        client.Ready += OnReadyAsync;
        client.SlashCommandExecuted += OnSlashCommandExecutedAsync;
        client.ButtonExecuted += OnButtonExecutedAsync;
        client.AutocompleteExecuted += OnAutocompleteExecutedAsync;
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
                .AddOption(SymbolOption("銘柄コード")))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("remove")
                .WithDescription("銘柄をウォッチリストから外します")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption(SymbolOption("銘柄コード")))
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
                .AddOption(SymbolOption("4桁の銘柄コード")))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("articles")
                .WithDescription("APIから取得済みの記事データを銘柄別に表示します")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption(SymbolOption("4桁の銘柄コード"))
                .AddOption("limit", ApplicationCommandOptionType.Integer, "表示する記事数", isRequired: false))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("prefetch")
                .WithDescription("設定上限の範囲で未取得データを取得します")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("limit", ApplicationCommandOptionType.Integer, "最大取得リクエスト数", isRequired: false));

        await guild.BulkOverwriteApplicationCommandAsync([import.Build(), watch.Build(), report.Build(), marketData.Build()]);
        logger.LogInformation("Slash commands registered.");
    }

    private static SlashCommandOptionBuilder SymbolOption(string description) =>
        new SlashCommandOptionBuilder()
            .WithName("symbol")
            .WithDescription(description)
            .WithType(ApplicationCommandOptionType.String)
            .WithRequired(true)
            .WithAutocomplete(true);

    private async Task OnSlashCommandExecutedAsync(SocketSlashCommand command)
    {
        if (options.Value.ChannelId != 0 && command.ChannelId != options.Value.ChannelId)
        {
            await command.RespondAsync(embed: DiscordEmbedFactory.Warning("利用できないチャンネルです", "このBotは設定済みのDiscordチャンネルでのみ利用できます。"), ephemeral: true);
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
            var embed = DiscordEmbedFactory.Error("コマンド処理に失敗しました", ex.Message);
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

    private async Task OnButtonExecutedAsync(SocketMessageComponent component)
    {
        try
        {
            using var scope = services.CreateScope();
            var id = component.Data.CustomId;
            if (id.StartsWith("watch:list:", StringComparison.Ordinal))
            {
                await UpdateWatchListComponentAsync(scope.ServiceProvider, component, ParsePage(id));
                return;
            }

            if (id.StartsWith("watch:targets:", StringComparison.Ordinal))
            {
                await UpdateWatchTargetsComponentAsync(scope.ServiceProvider, component, ParsePage(id), id.Contains(":unresolved:", StringComparison.Ordinal));
                return;
            }

            if (id.StartsWith("marketdata:coverage:", StringComparison.Ordinal))
            {
                await UpdateMarketDataCoverageComponentAsync(scope.ServiceProvider, component, ParsePage(id), id.Contains(":missing:", StringComparison.Ordinal));
                return;
            }

            if (id.StartsWith("marketdata:articles:", StringComparison.Ordinal))
            {
                var segments = id.Split(':');
                var symbol = segments.Length > 2 ? segments[2] : "";
                var limit = ParseLimit(id);
                await UpdateMarketDataArticlesComponentAsync(scope.ServiceProvider, component, symbol, limit, ParsePage(id));
                return;
            }

            if (id.StartsWith("hint:", StringComparison.Ordinal))
            {
                await component.RespondAsync(embed: DiscordEmbedFactory.Info("次の操作", BuildHintMessage(id)), ephemeral: true);
                return;
            }

            await component.RespondAsync(embed: DiscordEmbedFactory.Warning("未対応の操作です", "このボタンは現在のMVPでは操作案内のみです。"), ephemeral: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Component command failed.");
            await component.RespondAsync(embed: DiscordEmbedFactory.Error("ボタン処理に失敗しました", ex.Message), ephemeral: true);
        }
    }

    private async Task OnAutocompleteExecutedAsync(SocketAutocompleteInteraction interaction)
    {
        try
        {
            using var scope = services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IWatchlistService>();
            var current = interaction.Data.Current.Value?.ToString() ?? "";
            var normalized = SymbolNormalizer.NormalizeRaw(current);
            var candidates = (await service.ListTargetsAsync(CancellationToken.None))
                .Select(target => new { target.Symbol, target.Name })
                .Concat((await service.ListAsync(CancellationToken.None)).Select(item => new { item.Symbol, item.Name }))
                .GroupBy(item => item.Symbol)
                .Select(group => group.First())
                .Where(item =>
                    string.IsNullOrWhiteSpace(normalized) ||
                    item.Symbol.StartsWith(normalized, StringComparison.OrdinalIgnoreCase) ||
                    item.Name.Contains(current, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Symbol)
                .Take(25)
                .Select(item => new AutocompleteResult($"{item.Symbol} {item.Name}", item.Symbol));

            await interaction.RespondAsync(candidates);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Autocomplete failed.");
            await interaction.RespondAsync([]);
        }
    }

    private static async Task HandleImportAsync(IServiceProvider provider, SocketSlashCommand command)
    {
        await command.DeferAsync();
        var attachment = command.Data.Options.FirstOrDefault(o => o.Name == "file")?.Value as Attachment;
        if (attachment is null)
        {
            await command.FollowupAsync(embed: DiscordEmbedFactory.Warning("CSVファイルがありません", "SBI証券の保有銘柄CSVを添付してください。投資信託CSVや約定履歴CSVではない可能性も確認してください。"));
            return;
        }

        if (!attachment.Filename.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            await command.FollowupAsync(embed: DiscordEmbedFactory.Warning("CSVファイルを添付してください", $"`{attachment.Filename}` は `.csv` ではありません。SBI証券の保有銘柄CSVを添付してください。"));
            return;
        }

        using var http = new HttpClient();
        await using var stream = await http.GetStreamAsync(attachment.Url);
        var imports = provider.GetRequiredService<IImportService>();
        var result = await imports.ImportSbiCsvAsync(stream, attachment.Filename, CancellationToken.None);
        var embed = BuildImportEmbed(result);

        await command.FollowupAsync(embed: embed, components: result.Succeeded ? DiscordComponentFactory.ImportNextActions() : null);
    }

    private static async Task HandleWatchAsync(IServiceProvider provider, SocketSlashCommand command)
    {
        var sub = command.Data.Options.First();
        var service = provider.GetRequiredService<IWatchlistService>();
        if (sub.Name == "list")
        {
            var items = await service.ListAsync(CancellationToken.None);
            var view = BuildWatchListView(items, 0);
            await command.RespondAsync(embed: view.Embed, components: view.Components);
            return;
        }

        if (sub.Name == "targets")
        {
            var targets = await service.ListTargetsAsync(CancellationToken.None);
            var view = BuildWatchTargetsView(targets, 0, unresolvedOnly: false);
            await command.RespondAsync(embed: view.Embed, components: view.Components);
            return;
        }

        var input = sub.Options.First(o => o.Name == "symbol").Value.ToString() ?? "";
        var normalized = SymbolNormalizer.NormalizeJapaneseStockSymbol(input);
        if (!normalized.IsValid)
        {
            await command.RespondAsync(embed: DiscordEmbedFactory.Warning("銘柄コードを確認してください", normalized.ErrorMessage ?? "日本株4桁コードを入力してください。"), ephemeral: true);
            return;
        }

        var result = sub.Name == "add"
            ? await service.AddAsync(normalized.Symbol, CancellationToken.None)
            : await service.RemoveAsync(normalized.Symbol, CancellationToken.None);
        var mutationTargets = await service.ListTargetsAsync(CancellationToken.None);
        var target = mutationTargets.FirstOrDefault(item => item.Symbol == normalized.Symbol);
        var title = sub.Name == "add"
            ? (result.Succeeded ? "ウォッチリストに追加しました" : "ウォッチリスト追加を確認してください")
            : (result.Succeeded ? "ウォッチリストから削除しました" : "ウォッチリスト削除を確認してください");
        var description = BuildWatchMutationDescription(normalized.Symbol, result, target);
        await command.RespondAsync(embed: DiscordEmbedFactory.Create(title, description, result.Succeeded ? DiscordColors.Success : DiscordColors.Warning), components: DiscordComponentFactory.WatchActions(normalized.Symbol));
    }

    private async Task HandleReportAsync(IServiceProvider provider, SocketSlashCommand command)
    {
        await command.DeferAsync();
        var service = provider.GetRequiredService<IReportService>();
        var ran = await coordinator.TryRunAsync(async ct =>
        {
            var result = await service.GenerateDailyReportAsync(postToDiscord: false, ct);
            var embed = BuildReportEmbed(result);
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(result.Content));
            var fileName = $"investment-report-{DateTime.UtcNow:yyyy-MM-dd}.md";
            await command.FollowupWithFileAsync(stream, fileName, embed: embed, components: DiscordComponentFactory.ReportActions());
        }, CancellationToken.None);

        if (!ran)
        {
            await command.FollowupAsync(embed: DiscordEmbedFactory.Warning("レポート生成中", "レポート生成はすでに実行中です。完了後にもう一度確認してください。"));
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
            await UpdateDeferredResponseAsync(command, BuildMarketDataStatusEmbed(status), DiscordComponentFactory.MarketDataStatus());
            return;
        }

        if (sub.Name == "coverage")
        {
            var coverage = await service.GetCoverageAsync(CancellationToken.None);
            var view = BuildMarketDataCoverageView(coverage, 0, missingOnly: false);
            await UpdateDeferredResponseAsync(command, view.Embed, view.Components);
            return;
        }

        if (sub.Name == "data")
        {
            var symbol = NormalizeCommandSymbol(sub.Options.First(option => option.Name == "symbol").Value.ToString());
            if (!symbol.IsValid)
            {
                await UpdateDeferredResponseAsync(command, DiscordEmbedFactory.Warning("銘柄コードを確認してください", symbol.ErrorMessage ?? "日本株4桁コードを入力してください。"));
                return;
            }

            var detail = await service.GetDetailAsync(symbol.Symbol, CancellationToken.None);
            await UpdateDeferredResponseAsync(command, BuildMarketDataDetailEmbed(detail), detail.Found ? DiscordComponentFactory.MarketDataDetail(symbol.Symbol) : null);
            return;
        }

        if (sub.Name == "articles")
        {
            var symbol = NormalizeCommandSymbol(sub.Options.First(option => option.Name == "symbol").Value.ToString());
            if (!symbol.IsValid)
            {
                await UpdateDeferredResponseAsync(command, DiscordEmbedFactory.Warning("銘柄コードを確認してください", symbol.ErrorMessage ?? "日本株4桁コードを入力してください。"));
                return;
            }

            var articleLimit = ParseOptionLimit(sub, defaultValue: 10, min: 1, max: 20);
            var detail = await service.GetDetailAsync(symbol.Symbol, CancellationToken.None);
            var view = BuildMarketDataArticlesView(detail, symbol.Symbol, articleLimit, 0);
            await UpdateDeferredResponseAsync(command, view.Embed, view.Components);
            return;
        }

        await UpdateDeferredResponseAsync(command, DiscordEmbedFactory.Processing("市場データ取得中...", "対象データの事前取得を開始しました。完了まで少し待ってください。"));
        var prefetchLimit = ParseOptionLimit(sub, defaultValue: 0, min: 0, max: int.MaxValue);
        var result = await service.PrefetchAsync(prefetchLimit == 0 ? null : prefetchLimit, CancellationToken.None);
        await UpdateDeferredResponseAsync(command, BuildPrefetchEmbed(result), DiscordComponentFactory.PrefetchResult());
    }

    private static SymbolNormalizationResult NormalizeCommandSymbol(string? value) =>
        SymbolNormalizer.NormalizeJapaneseStockSymbol(value);

    private static async Task UpdateWatchListComponentAsync(IServiceProvider provider, SocketMessageComponent component, int page)
    {
        var service = provider.GetRequiredService<IWatchlistService>();
        var view = BuildWatchListView(await service.ListAsync(CancellationToken.None), page);
        await UpdateComponentAsync(component, view.Embed, view.Components);
    }

    private static async Task UpdateWatchTargetsComponentAsync(IServiceProvider provider, SocketMessageComponent component, int page, bool unresolvedOnly)
    {
        var service = provider.GetRequiredService<IWatchlistService>();
        var view = BuildWatchTargetsView(await service.ListTargetsAsync(CancellationToken.None), page, unresolvedOnly);
        await UpdateComponentAsync(component, view.Embed, view.Components);
    }

    private static async Task UpdateMarketDataCoverageComponentAsync(IServiceProvider provider, SocketMessageComponent component, int page, bool missingOnly)
    {
        var service = provider.GetRequiredService<IMarketDataPrefetchService>();
        var view = BuildMarketDataCoverageView(await service.GetCoverageAsync(CancellationToken.None), page, missingOnly);
        await UpdateComponentAsync(component, view.Embed, view.Components);
    }

    private static async Task UpdateMarketDataArticlesComponentAsync(IServiceProvider provider, SocketMessageComponent component, string symbol, int limit, int page)
    {
        var service = provider.GetRequiredService<IMarketDataPrefetchService>();
        var detail = await service.GetDetailAsync(symbol, CancellationToken.None);
        var view = BuildMarketDataArticlesView(detail, symbol, limit, page);
        await UpdateComponentAsync(component, view.Embed, view.Components);
    }

    private static Embed BuildImportEmbed(SbiImportResult result)
    {
        var description = DiscordFormatters.JoinNonEmpty(
            result.Message,
            "",
            "結果",
            $"保有銘柄: {result.ImportedCount}件",
            $"新規: {result.CreatedCount}件 / 更新: {result.UpdatedCount}件",
            $"売却検出: {result.SoldDetectedCount}件",
            $"ウォッチリスト自動追加: {result.WatchlistAddedCount}件",
            $"スキップ: {result.SkippedCount}件",
            result.SkippedCount > 0 ? "スキップされた行があります。CSVの対象銘柄と形式を確認してください。" : null,
            "",
            "ファイル",
            string.IsNullOrWhiteSpace(result.SourceCsvFileName) ? "不明" : result.SourceCsvFileName,
            $"文字コード: {(string.IsNullOrWhiteSpace(result.EncodingName) ? "不明" : result.EncodingName)}",
            $"取り込み時刻: {DiscordFormatters.FormatJst(result.ImportedAt)}",
            $"ImportBatch ID: {result.ImportBatchId?.ToString() ?? "なし"}",
            "",
            "次の操作: `/watch targets` -> `/marketdata prefetch` -> `/report`");
        return DiscordEmbedFactory.Create(result.Succeeded ? "CSV取り込み完了" : "CSV取り込み失敗", description, result.Succeeded && result.SkippedCount == 0 ? DiscordColors.Success : DiscordColors.Warning);
    }

    private static (Embed Embed, MessageComponent? Components) BuildWatchListView(IReadOnlyList<WatchlistItemDto> items, int requestedPage)
    {
        var totalPages = DiscordFormatters.PageCount(items.Count, DiscordFormatters.WatchPageSize);
        var page = Math.Clamp(requestedPage, 0, totalPages - 1);
        var pageItems = items
            .OrderBy(item => item.Source)
            .ThenBy(item => item.Symbol)
            .Skip(page * DiscordFormatters.WatchPageSize)
            .Take(DiscordFormatters.WatchPageSize)
            .ToList();

        var body = items.Count == 0
            ? "ウォッチリストは空です。\n次の例から始められます。\n`/watch add symbol:7203`\n`/import file:<csv>`"
            : string.Join("\n", pageItems.Select(item => $"`{item.Symbol}` {item.Name} / {DiscordFormatters.FormatWatchlistSource(item.Source)}{(item.IsHolding ? " / 保有中" : "")}"));
        var manual = items.Count(item => item.Source == WatchlistSource.Manual);
        var sold = items.Count(item => item.Source == WatchlistSource.SoldAutomatically);
        var holding = items.Count(item => item.IsHolding);
        var description = $"登録数: {items.Count}件\n手動追加: {manual}件\n売却検出: {sold}件\n保有中: {holding}件\nページ: {DiscordFormatters.PageLabel(page, totalPages)}\n\n{body}";
        return (
            DiscordEmbedFactory.Info("ウォッチリスト", description),
            DiscordComponentFactory.WatchList(page, totalPages));
    }

    private static (Embed Embed, MessageComponent? Components) BuildWatchTargetsView(IReadOnlyList<WatchTargetDto> targets, int requestedPage, bool unresolvedOnly)
    {
        var ordered = targets
            .OrderByDescending(target => !string.IsNullOrWhiteSpace(target.ExternalSymbolResolutionError) || string.IsNullOrWhiteSpace(target.ExternalSymbol))
            .ThenBy(target => target.Symbol)
            .ToList();
        var filtered = unresolvedOnly
            ? ordered.Where(target => !string.IsNullOrWhiteSpace(target.ExternalSymbolResolutionError) || string.IsNullOrWhiteSpace(target.ExternalSymbol)).ToList()
            : ordered;
        var totalPages = DiscordFormatters.PageCount(filtered.Count, DiscordFormatters.WatchPageSize);
        var page = Math.Clamp(requestedPage, 0, totalPages - 1);
        var pageItems = filtered.Skip(page * DiscordFormatters.WatchPageSize).Take(DiscordFormatters.WatchPageSize);
        var body = filtered.Count == 0
            ? "条件に一致する監視対象銘柄はありません。"
            : string.Join("\n\n", pageItems.Select(FormatWatchTarget));
        var unresolved = targets.Count(target => !string.IsNullOrWhiteSpace(target.ExternalSymbolResolutionError) || string.IsNullOrWhiteSpace(target.ExternalSymbol));
        var description = $"対象数: {targets.Count}件\n保有: {targets.Count(target => target.IsHolding)}件\nウォッチ: {targets.Count(target => target.IsWatchlisted)}件\n外部シンボル未解決: {unresolved}件\nページ: {DiscordFormatters.PageLabel(page, totalPages)}\n\n{body}";
        return (
            DiscordEmbedFactory.Info(unresolvedOnly ? "監視対象銘柄（未解決のみ）" : "監視対象銘柄", description),
            DiscordComponentFactory.WatchTargets(page, totalPages, unresolvedOnly));
    }

    private static string FormatWatchTarget(WatchTargetDto target)
    {
        var targetType = string.Join(" + ", new[]
        {
            target.IsHolding ? "保有" : null,
            target.IsWatchlisted ? "監視" : null
        }.Where(value => value is not null));
        var market = string.Join(" / ", new[] { target.Market, target.Country, target.Currency }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var external = string.IsNullOrWhiteSpace(target.ExternalSymbol) ? "外部: 未解決" : $"外部: {target.ExternalSymbol}";
        var source = target.WatchlistSource is null ? "" : $" / 登録元: {DiscordFormatters.FormatWatchlistSource(target.WatchlistSource.Value)}";
        var error = string.IsNullOrWhiteSpace(target.ExternalSymbolResolutionError) ? "" : $"\nエラー: {target.ExternalSymbolResolutionError}";
        return $"`{target.Symbol}` {target.Name}\n{targetType} / {DiscordFormatters.FormatSecurityType(target.SecurityType)}{(string.IsNullOrWhiteSpace(market) ? "" : $" / {market}")} / {external}{source}{error}";
    }

    private static string BuildWatchMutationDescription(string symbol, WatchlistMutationResult result, WatchTargetDto? target)
    {
        var status = target is null
            ? "状態: ウォッチリスト未登録"
            : $"状態: {(target.IsHolding ? "保有中" : "監視中")}{(target.IsWatchlisted ? " / ウォッチ中" : "")}";
        return DiscordFormatters.JoinNonEmpty(
            $"`{symbol}` {target?.Name ?? symbol}",
            result.Message,
            status,
            target is null ? null : $"種別: {DiscordFormatters.FormatSecurityType(target.SecurityType)}",
            target?.ExternalSymbol is null ? null : $"外部シンボル: {target.ExternalSymbol}",
            target?.IsHolding == true ? "保有中の銘柄は、ウォッチリストから削除してもレポート対象には残ります。" : null,
            "",
            "次の操作: `/marketdata data symbol:" + symbol + "` / `/marketdata articles symbol:" + symbol + "` / `/watch targets`");
    }

    private static Embed BuildReportEmbed(ReportResult result)
    {
        var builder = new EmbedBuilder()
            .WithTitle("投資判断レポート")
            .WithDescription(DiscordFormatters.Truncate(DiscordFormatters.BuildReportSummary(result), DiscordFormatters.EmbedDescriptionLimit))
            .WithColor(result.Succeeded ? DiscordColors.Report : DiscordColors.Error)
            .AddField("AnalysisRun ID", result.AnalysisRunId?.ToString() ?? "なし", true)
            .AddField("元ImportBatch ID", result.SourceImportBatchId?.ToString() ?? "なし", true)
            .AddField("CSV取り込み時刻", DiscordFormatters.FormatJst(result.SourceImportedAt), true)
            .AddField("Markdown", "全文を添付ファイルで出力しました。", false)
            .WithCurrentTimestamp();
        return builder.Build();
    }

    private static Embed BuildMarketDataStatusEmbed(MarketDataStatusResult status)
    {
        var usageRate = status.DailyLimit <= 0 ? 0m : (decimal)status.UsedToday / status.DailyLimit;
        var nextItems = status.NextItems.Take(10).ToList();
        var next = nextItems.Count == 0 ? "なし" : string.Join(", ", nextItems.Select(item => $"`{item}`"));
        if (status.NextItems.Count > 10)
        {
            next += $" 他{status.NextItems.Count - 10}件";
        }

        var color = status.RemainingToday <= 0 ? DiscordColors.Error : status.RemainingToday <= Math.Max(1, status.DailyLimit / 5) ? DiscordColors.Warning : DiscordColors.MarketData;
        var state = status.RemainingToday <= 0 ? "取得上限に達しています。" : status.RemainingToday <= Math.Max(1, status.DailyLimit / 5) ? "残りリクエストが少なくなっています。" : "まだ取得可能です。";
        var description = $"今日の使用状況\n{status.UsedToday} / {status.DailyLimit} requests\n使用率: {usageRate:P0}\n残り: {status.RemainingToday}\n\n未取得キュー: {status.PendingCount}件\n\n次の取得候補\n{next}\n\n状態: {state}";
        return DiscordEmbedFactory.Create("市場データ取得状況", description, color);
    }

    private static (Embed Embed, MessageComponent? Components) BuildMarketDataCoverageView(MarketDataCoverageResult coverage, int requestedPage, bool missingOnly)
    {
        var ordered = coverage.Items
            .OrderByDescending(DiscordFormatters.HasCoverageMissing)
            .ThenBy(item => item.Symbol)
            .ToList();
        var filtered = missingOnly ? ordered.Where(DiscordFormatters.HasCoverageMissing).ToList() : ordered;
        var totalPages = DiscordFormatters.PageCount(filtered.Count, DiscordFormatters.CoveragePageSize);
        var page = Math.Clamp(requestedPage, 0, totalPages - 1);
        var pageItems = filtered.Skip(page * DiscordFormatters.CoveragePageSize).Take(DiscordFormatters.CoveragePageSize);
        var body = filtered.Count == 0
            ? "不足がある銘柄はありません。"
            : string.Join("\n\n", pageItems.Select(FormatCoverageItem));
        var description = $"対象: {coverage.TargetCount}件\n外部シンボル: {FormatCoverageCount(coverage.ProviderCoveredCount, coverage.TargetCount)}\n価格: {FormatCoverageCount(coverage.PriceCachedCount, coverage.TargetCount)}\n日足: {FormatCoverageCount(coverage.DailyCachedCount, coverage.TargetCount)}\nニュース: {FormatCoverageCount(coverage.NewsCachedCount, coverage.TargetCount)}\n為替: {FormatCoverageCount(coverage.ExchangeRateCachedCount, coverage.TargetCount)}\nページ: {DiscordFormatters.PageLabel(page, totalPages)}\n\n不足がある銘柄\n{body}";
        return (
            DiscordEmbedFactory.MarketData(missingOnly ? "市場データカバレッジ（不足のみ）" : "市場データカバレッジ", description),
            DiscordComponentFactory.MarketDataCoverage(page, totalPages, missingOnly));
    }

    private static string FormatCoverageCount(int count, int total) =>
        total <= 0 ? $"{count}/{total}" : $"{count}/{total} ({(decimal)count / total:P0})";

    private static string FormatCoverageItem(MarketDataCoverageItem item)
    {
        var error = string.IsNullOrWhiteSpace(item.ResolutionError) ? "" : $"\nエラー: {item.ResolutionError}";
        var external = string.IsNullOrWhiteSpace(item.ExternalSymbol) ? "" : $" / 外部: {item.ExternalSymbol}";
        return $"`{item.Symbol}` {item.Name}{external}\n外部 {DiscordFormatters.CoverageIcon(item.IsProviderCovered)} / 価格 {DiscordFormatters.CoverageIcon(item.HasFreshPrice)} / 日足 {DiscordFormatters.CoverageIcon(item.HasDailySeries)} / ニュース {DiscordFormatters.CoverageIcon(item.HasNewsSentiment)} / 為替 {DiscordFormatters.CoverageIcon(item.HasExchangeRate)}{error}";
    }

    private static Embed BuildMarketDataDetailEmbed(MarketDataDetailResult detail)
    {
        if (!detail.Found)
        {
            return DiscordEmbedFactory.Warning("API取得データ", detail.Message ?? "銘柄が見つかりません。入力例: 7203");
        }

        var latestPrices = detail.DailyPrices.Count == 0
            ? "なし"
            : string.Join("\n", detail.DailyPrices
                .OrderByDescending(price => price.Date)
                .Take(5)
                .Select(price => $"- `{price.Date:yyyy-MM-dd}` 終値 {price.Close:N2} / 出来高 {price.Volume:N0}"));
        var latestPriceFetchedAt = detail.CacheEntries
            .Where(entry => entry.Function.Contains("Price", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.FetchedAt)
            .Select(entry => DiscordFormatters.FormatJst(entry.FetchedAt))
            .FirstOrDefault() ?? "なし";
        var latestNewsFetchedAt = detail.News
            .OrderByDescending(news => news.FetchedAt ?? news.PublishedAt)
            .Select(news => DiscordFormatters.FormatJst(news.FetchedAt ?? news.PublishedAt))
            .FirstOrDefault() ?? "なし";

        return new EmbedBuilder()
            .WithTitle($"API取得データ: {detail.Symbol} {detail.Name}")
            .WithColor(DiscordColors.MarketData)
            .WithDescription($"価格: {(detail.CacheEntries.Any(entry => entry.Function.Contains("Price", StringComparison.OrdinalIgnoreCase) && entry.Succeeded) ? "取得済み" : "未取得")}\n日足: {(detail.DailyPrices.Count > 0 ? "取得済み" : "未取得")}\n財務: {(detail.FinancialSnapshot is null ? "未取得" : "取得済み")}\nニュース: {detail.News.Count}件\nキャッシュ: {detail.CacheEntries.Count}件\n\n最終更新:\n価格: {latestPriceFetchedAt}\nニュース: {latestNewsFetchedAt}")
            .AddField("日足", DiscordFormatters.Truncate(latestPrices, DiscordFormatters.EmbedFieldLimit), false)
            .AddField("財務", DiscordFormatters.Truncate(FormatFinancialSnapshot(detail.FinancialSnapshot, detail.Message), DiscordFormatters.EmbedFieldLimit), false)
            .AddField("ニュース", DiscordFormatters.Truncate(FormatNews(detail.News), DiscordFormatters.EmbedFieldLimit), false)
            .AddField("APIキャッシュ", DiscordFormatters.Truncate(FormatCacheEntries(detail.CacheEntries), DiscordFormatters.EmbedFieldLimit), false)
            .WithCurrentTimestamp()
            .Build();
    }

    private static (Embed Embed, MessageComponent? Components) BuildMarketDataArticlesView(MarketDataDetailResult detail, string symbol, int limit, int requestedPage)
    {
        if (!detail.Found)
        {
            return (DiscordEmbedFactory.Warning("API取得記事", detail.Message ?? "銘柄が見つかりません。入力例: 7203"), null);
        }

        var articles = detail.News
            .OrderByDescending(item => item.RelevanceScore)
            .ThenByDescending(item => item.PublishedAt ?? item.FetchedAt)
            .Take(limit)
            .ToList();
        var totalPages = DiscordFormatters.PageCount(articles.Count, DiscordFormatters.ArticlePageSize);
        var page = Math.Clamp(requestedPage, 0, totalPages - 1);
        var positive = detail.News.Count(item => item.SentimentScore > 0);
        var negative = detail.News.Count(item => item.SentimentScore < 0);
        var neutral = detail.News.Count - positive - negative;
        var averageRelevance = detail.News.Count == 0 ? 0m : detail.News.Average(item => item.RelevanceScore);
        var description = $"記事数: {detail.News.Count}件\nPositive: {positive}\nNeutral: {neutral}\nNegative: {negative}\n平均関連度: {averageRelevance:P0}\nページ: {DiscordFormatters.PageLabel(page, totalPages)}\n\n重要そうな記事\n\n{DiscordFormatters.FormatArticles(articles, page, DiscordFormatters.ArticlePageSize)}";
        return (
            DiscordEmbedFactory.Create($"API取得記事: {detail.Symbol} {detail.Name}", description, detail.News.Count > 0 ? DiscordColors.MarketData : DiscordColors.Warning),
            DiscordComponentFactory.MarketDataArticles(symbol, page, totalPages, limit));
    }

    private static Embed BuildPrefetchEmbed(MarketDataPrefetchResult result)
    {
        var failed = result.RequestLogs.Count(log => !log.Succeeded);
        var color = failed > 0 ? DiscordColors.Warning : result.Succeeded > 0 ? DiscordColors.Success : DiscordColors.Warning;
        var messages = result.Messages.Count == 0 ? "なし" : string.Join("\n", result.Messages.Select(message => $"- {message}"));
        var description = $"試行: {result.Attempted}件\n成功: {result.Succeeded}件\n失敗: {failed}件\nスキップ: {result.Skipped}件\n\n今日の使用数: {result.UsedToday}\n今日の残り: {result.RemainingToday}\n{(result.RemainingToday <= 0 ? "\n残りリクエストが0です。次回のリセット後に再実行してください。\n" : "")}\nメッセージ\n{DiscordFormatters.Truncate(messages, 700)}\n\n失敗優先ログ\n{DiscordFormatters.Truncate(DiscordFormatters.FormatRequestLogs(result.RequestLogs), 1200)}";
        return DiscordEmbedFactory.Create(failed > 0 ? "市場データ事前取得完了（警告あり）" : "市場データ事前取得完了", description, color);
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
            .OrderByDescending(item => item.PublishedAt ?? item.FetchedAt)
            .Take(5)
            .Select(item =>
            {
                var published = item.PublishedAt is null ? "" : $" `{DiscordFormatters.FormatJst(item.PublishedAt)}`";
                var score = item.SentimentScore > 0 ? "Positive" : item.SentimentScore < 0 ? "Negative" : "Neutral";
                return $"-{published} {score}: {item.Title}";
            }));
    }

    private static string FormatCacheEntries(IReadOnlyList<ExternalApiCacheSummary> entries)
    {
        if (entries.Count == 0)
        {
            return "なし";
        }

        var lines = entries
            .OrderByDescending(entry => entry.FetchedAt)
            .Take(8)
            .Select(entry =>
            {
                var status = entry.Succeeded ? "OK" : "NG";
                var error = string.IsNullOrWhiteSpace(entry.ErrorMessage) ? "" : $" / {entry.ErrorMessage}";
                return $"- `{DiscordFormatters.FormatJst(entry.FetchedAt)}` {status} {entry.Provider}:{entry.Function} ({entry.PayloadLength:N0} bytes){error}";
            })
            .ToList();

        if (entries.Count > 8)
        {
            lines.Add($"- 他 {entries.Count - 8} 件");
        }

        return string.Join("\n", lines);
    }

    private static Task UpdateDeferredResponseAsync(SocketSlashCommand command, Embed embed, MessageComponent? components = null) =>
        command.ModifyOriginalResponseAsync(message =>
        {
            message.Content = "";
            message.Embed = embed;
            message.Components = components;
        });

    private static Task UpdateComponentAsync(SocketMessageComponent component, Embed embed, MessageComponent? components = null) =>
        component.UpdateAsync(message =>
        {
            message.Content = "";
            message.Embed = embed;
            message.Components = components;
        });

    private static int ParseOptionLimit(SocketSlashCommandDataOption sub, int defaultValue, int min, int max)
    {
        var limitValue = sub.Options.FirstOrDefault(option => option.Name == "limit")?.Value;
        return limitValue is long longValue ? (int)Math.Clamp(longValue, min, max) : defaultValue;
    }

    private static int ParsePage(string customId)
    {
        var segments = customId.Split(':');
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (segments[index] == "page" && int.TryParse(segments[index + 1], out var page))
            {
                return Math.Max(0, page);
            }
        }

        return 0;
    }

    private static int ParseLimit(string customId)
    {
        var segments = customId.Split(':');
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (segments[index] == "limit" && int.TryParse(segments[index + 1], out var limit))
            {
                return Math.Clamp(limit, 1, 20);
            }
        }

        return 10;
    }

    private static string BuildHintMessage(string customId) => customId switch
    {
        "hint:marketdata:prefetch" => DiscordFormatters.BuildCommandHint("/marketdata prefetch"),
        "hint:report" => DiscordFormatters.BuildCommandHint("/report"),
        var id when id.StartsWith("hint:marketdata:data:", StringComparison.Ordinal) => DiscordFormatters.BuildCommandHint($"/marketdata data symbol:{id.Split(':').Last()}"),
        var id when id.StartsWith("hint:marketdata:articles:", StringComparison.Ordinal) => DiscordFormatters.BuildCommandHint($"/marketdata articles symbol:{id.Split(':').Last()}"),
        "hint:report:markdown" => "レポート全文はMarkdown添付ファイルで出力済みです。",
        _ => "案内用ボタンです。表示されているコマンドを実行してください。"
    };
}
