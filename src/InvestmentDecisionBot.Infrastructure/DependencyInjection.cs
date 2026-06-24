using Discord.WebSocket;
using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Infrastructure.Csv;
using InvestmentDecisionBot.Infrastructure.Discord;
using InvestmentDecisionBot.Infrastructure.Persistence;
using InvestmentDecisionBot.Infrastructure.Providers;
using InvestmentDecisionBot.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InvestmentDecisionBot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var databaseProvider = configuration["DATABASE_PROVIDER"] ?? "Sqlite";
        var databasePath = configuration["DATABASE_PATH"];
        if (databaseProvider.Equals("InMemory", StringComparison.OrdinalIgnoreCase))
        {
            var databaseName = configuration["INMEMORY_DATABASE_NAME"];
            if (string.IsNullOrWhiteSpace(databaseName))
            {
                databaseName = "investment-decision-bot-debug";
            }

            services.AddDbContext<BotDbContext>(options => options.UseInMemoryDatabase(databaseName));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                databasePath = configuration.GetConnectionString("Default") ?? "data/investment-decision-bot.db";
            }

            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            services.AddDbContext<BotDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
        }

        services.AddScoped<IBotDbContext>(provider => provider.GetRequiredService<BotDbContext>());
        services.AddScoped<ISbiCsvParser, SbiCsvParser>();
        services.AddScoped<ISystemLogService, SystemLogService>();
        services.AddScoped<IMarketDataProvider, NullMarketDataProvider>();
        services.AddScoped<INewsProvider, NullNewsProvider>();
        services.AddScoped<IFinancialDataProvider, NullFinancialDataProvider>();
        services.AddScoped<IExchangeRateProvider, NullExchangeRateProvider>();
        services.Configure<OpenAiOptions>(options =>
        {
            options.Enabled = bool.TryParse(configuration["OPENAI_ENABLED"], out var enabled) && enabled;
            options.ApiKey = configuration["OPENAI_API_KEY"] ?? "";
            options.Model = configuration["OPENAI_MODEL"] ?? "gpt-4.1-mini";
        });
        services.AddHttpClient<IAiAnalysisClient, OpenAiAnalysisClient>();
        services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig { GatewayIntents = global::Discord.GatewayIntents.Guilds }));
        services.Configure<DiscordOptions>(options =>
        {
            options.Token = configuration["DISCORD_TOKEN"] ?? "";
            _ = ulong.TryParse(configuration["DISCORD_GUILD_ID"], out var guildId);
            _ = ulong.TryParse(configuration["DISCORD_CHANNEL_ID"], out var channelId);
            options.GuildId = guildId;
            options.ChannelId = channelId;
        });
        services.AddScoped<IDiscordReportPublisher, DiscordReportPublisher>();
        return services;
    }
}
