using Discord;
using Discord.WebSocket;
using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Application.DTOs;
using Microsoft.Extensions.Options;

namespace InvestmentDecisionBot.Infrastructure.Discord;

public sealed class DiscordReportPublisher(DiscordSocketClient client, IOptions<DiscordOptions> options) : IDiscordReportPublisher
{
    public async Task<DiscordPostResult> PostReportAsync(string content, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.Token) || options.Value.ChannelId == 0)
        {
            return new DiscordPostResult(false, null, "Discord token or channel id is not configured.");
        }

        var channel = client.GetChannel(options.Value.ChannelId) as IMessageChannel;
        if (channel is null)
        {
            return new DiscordPostResult(false, null, "Discord channel was not found.");
        }

        var message = await channel.SendMessageAsync(content[..Math.Min(content.Length, 1900)]);
        return new DiscordPostResult(true, message.Id.ToString(), null);
    }
}
