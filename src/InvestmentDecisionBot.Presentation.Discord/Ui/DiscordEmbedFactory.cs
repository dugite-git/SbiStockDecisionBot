using Discord;

namespace InvestmentDecisionBot.Presentation.Discord.Ui;

public static class DiscordEmbedFactory
{
    public static Embed Success(string title, string description) =>
        Create(title, description, DiscordColors.Success);

    public static Embed Warning(string title, string description) =>
        Create(title, description, DiscordColors.Warning);

    public static Embed Error(string title, string description) =>
        Create(title, description, DiscordColors.Error);

    public static Embed Info(string title, string description) =>
        Create(title, description, DiscordColors.Info);

    public static Embed Processing(string title, string description) =>
        Create(title, description, DiscordColors.Info);

    public static Embed MarketData(string title, string description) =>
        Create(title, description, DiscordColors.MarketData);

    public static Embed Report(string title, string description) =>
        Create(title, description, DiscordColors.Report);

    public static Embed Create(string title, string description, Color color) =>
        new EmbedBuilder()
            .WithTitle(title)
            .WithDescription(DiscordFormatters.Truncate(description, DiscordFormatters.EmbedDescriptionLimit))
            .WithColor(color)
            .WithCurrentTimestamp()
            .Build();
}
