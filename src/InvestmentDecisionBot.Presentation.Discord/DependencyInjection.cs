using Discord.WebSocket;
using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Presentation.Discord.HostedServices;
using InvestmentDecisionBot.Presentation.Discord.Options;
using InvestmentDecisionBot.Presentation.Discord.Publishing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InvestmentDecisionBot.Presentation.Discord;

public static class DependencyInjection
{
    public static IServiceCollection AddDiscordPresentation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = global::Discord.GatewayIntents.Guilds
        }));

        services.Configure<DiscordOptions>(options =>
        {
            options.Token = configuration["DISCORD_TOKEN"] ?? "";
            _ = ulong.TryParse(configuration["DISCORD_GUILD_ID"], out var guildId);
            _ = ulong.TryParse(configuration["DISCORD_CHANNEL_ID"], out var channelId);
            options.GuildId = guildId;
            options.ChannelId = channelId;
        });

        services.AddScoped<IDiscordReportPublisher, DiscordReportPublisher>();
        services.AddHostedService<DiscordBotHostedService>();
        return services;
    }
}
