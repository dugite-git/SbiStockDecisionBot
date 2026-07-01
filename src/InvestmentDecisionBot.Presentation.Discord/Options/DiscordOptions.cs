namespace InvestmentDecisionBot.Presentation.Discord.Options;

public sealed class DiscordOptions
{
    public string Token { get; set; } = "";
    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }
}
