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
            .WithDescription("SBI CSVから保有株を同期します")
            .AddOption("file", ApplicationCommandOptionType.Attachment, "SBI CSV file", isRequired: true);

        var watch = new SlashCommandBuilder()
            .WithName("watch")
            .WithDescription("Watchlistを管理します")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("add")
                .WithDescription("銘柄をWatchlistに追加します")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("symbol", ApplicationCommandOptionType.String, "銘柄コード", isRequired: true))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("remove")
                .WithDescription("銘柄をWatchlistから外します")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("symbol", ApplicationCommandOptionType.String, "銘柄コード", isRequired: true))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("list")
                .WithDescription("Watchlistを表示します")
                .WithType(ApplicationCommandOptionType.SubCommand));

        var report = new SlashCommandBuilder()
            .WithName("report")
            .WithDescription("投資判断レポートを生成します");

        await guild.BulkOverwriteApplicationCommandAsync([import.Build(), watch.Build(), report.Build()]);
        logger.LogInformation("Slash commands registered.");
    }

    private async Task OnSlashCommandExecutedAsync(SocketSlashCommand command)
    {
        if (options.Value.ChannelId != 0 && command.ChannelId != options.Value.ChannelId)
        {
            await command.RespondAsync("このBotは設定されたDiscordチャンネルでのみ利用できます。", ephemeral: true);
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
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Slash command failed.");
            if (!command.HasResponded)
            {
                await command.RespondAsync($"処理に失敗しました: {ex.Message}", ephemeral: true);
            }
            else
            {
                await command.FollowupAsync($"処理に失敗しました: {ex.Message}", ephemeral: true);
            }
        }
    }

    private static async Task HandleImportAsync(IServiceProvider provider, SocketSlashCommand command)
    {
        await command.DeferAsync();
        var attachment = command.Data.Options.FirstOrDefault(o => o.Name == "file")?.Value as Attachment;
        if (attachment is null)
        {
            await command.FollowupAsync("CSVファイルを添付してください。");
            return;
        }

        using var http = new HttpClient();
        await using var stream = await http.GetStreamAsync(attachment.Url);
        var imports = provider.GetRequiredService<IImportService>();
        var result = await imports.ImportSbiCsvAsync(stream, attachment.Filename, CancellationToken.None);
        await command.FollowupAsync($"""
CSVインポート{(result.Succeeded ? "完了" : "失敗")}
- 取込件数: {result.ImportedCount}
- 新規追加: {result.CreatedCount}
- 更新: {result.UpdatedCount}
- 売却済み検出: {result.SoldDetectedCount}
- Watchlist追加: {result.WatchlistAddedCount}
- Encoding: {result.EncodingName}
- Message: {result.Message}
""");
    }

    private static async Task HandleWatchAsync(IServiceProvider provider, SocketSlashCommand command)
    {
        var sub = command.Data.Options.First();
        var service = provider.GetRequiredService<IWatchlistService>();
        if (sub.Name == "list")
        {
            var items = await service.ListAsync(CancellationToken.None);
            var content = items.Count == 0
                ? "Watchlistは空です。"
                : "Watchlist\n" + string.Join("\n", items.Select(i => $"- {i.Symbol} {i.Name} / {i.Source}{(i.IsHolding ? " / Holding" : "")}"));
            await command.RespondAsync(content);
            return;
        }

        var symbol = sub.Options.First(o => o.Name == "symbol").Value.ToString() ?? "";
        var result = sub.Name == "add"
            ? await service.AddAsync(symbol, CancellationToken.None)
            : await service.RemoveAsync(symbol, CancellationToken.None);
        await command.RespondAsync(result.Message);
    }

    private async Task HandleReportAsync(IServiceProvider provider, SocketSlashCommand command)
    {
        await command.DeferAsync();
        var service = provider.GetRequiredService<IReportService>();
        var ran = await coordinator.TryRunAsync(async ct =>
        {
            var result = await service.GenerateDailyReportAsync(postToDiscord: false, ct);
            await command.FollowupAsync(result.Content[..Math.Min(result.Content.Length, 1900)]);
        }, CancellationToken.None);

        if (!ran)
        {
            await command.FollowupAsync("レポート生成が既に実行中です。");
        }
    }
}
