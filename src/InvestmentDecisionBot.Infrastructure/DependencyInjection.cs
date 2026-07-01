using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Infrastructure.Csv;
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
        services.AddHttpClient("ExternalMarketData", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        var marketDataProvider = configuration["MARKET_DATA_PROVIDER"];
        var selectedMarketDataProvider = string.IsNullOrWhiteSpace(marketDataProvider) ? "JQuants" : marketDataProvider;
        if (selectedMarketDataProvider.Equals("Null", StringComparison.OrdinalIgnoreCase) ||
            selectedMarketDataProvider.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<NullMarketDataProvider>();
            services.AddScoped<IMarketDataProvider>(provider => provider.GetRequiredService<NullMarketDataProvider>());
            services.AddScoped<ICachedMarketDataProvider>(provider => provider.GetRequiredService<NullMarketDataProvider>());
            services.AddScoped<IMarketDataPrefetchService>(provider => provider.GetRequiredService<NullMarketDataProvider>());
        }
        else if (selectedMarketDataProvider.Equals("JQuants", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<JQuantsMarketDataProvider>();
            services.AddScoped<IMarketDataProvider>(provider => provider.GetRequiredService<JQuantsMarketDataProvider>());
            services.AddScoped<ICachedMarketDataProvider>(provider => provider.GetRequiredService<JQuantsMarketDataProvider>());
            services.AddScoped<IFinancialDataProvider>(provider => provider.GetRequiredService<JQuantsMarketDataProvider>());
            services.AddScoped<IMarketDataPrefetchService>(provider => provider.GetRequiredService<JQuantsMarketDataProvider>());
        }
        else
        {
            throw new InvalidOperationException($"Unsupported MARKET_DATA_PROVIDER '{selectedMarketDataProvider}'. Register a provider implementation before enabling it.");
        }

        services.AddScoped<INewsProvider, NullNewsProvider>();
        if (selectedMarketDataProvider.Equals("Null", StringComparison.OrdinalIgnoreCase) ||
            selectedMarketDataProvider.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IFinancialDataProvider, NullFinancialDataProvider>();
        }
        services.AddScoped<IExchangeRateProvider, NullExchangeRateProvider>();
        return services;
    }
}
