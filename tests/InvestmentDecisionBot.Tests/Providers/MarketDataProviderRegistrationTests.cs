using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Infrastructure;
using InvestmentDecisionBot.Infrastructure.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InvestmentDecisionBot.Tests.Providers;

public sealed class MarketDataProviderRegistrationTests
{
    [Fact]
    public void UsesJQuantsProviderWhenMarketDataProviderIsUnset()
    {
        using var services = BuildProvider([]);

        Assert.IsType<JQuantsMarketDataProvider>(services.GetRequiredService<IMarketDataProvider>());
        Assert.IsType<JQuantsMarketDataProvider>(services.GetRequiredService<ICachedMarketDataProvider>());
        Assert.IsType<JQuantsMarketDataProvider>(services.GetRequiredService<IFinancialDataProvider>());
        Assert.IsType<JQuantsMarketDataProvider>(services.GetRequiredService<IMarketDataPrefetchService>());
    }

    [Fact]
    public void UsesNullProviderWhenMarketDataProviderIsDisabled()
    {
        using var services = BuildProvider([new KeyValuePair<string, string?>("MARKET_DATA_PROVIDER", "Disabled")]);

        Assert.IsType<NullMarketDataProvider>(services.GetRequiredService<IMarketDataProvider>());
        Assert.IsType<NullMarketDataProvider>(services.GetRequiredService<ICachedMarketDataProvider>());
        Assert.IsType<NullFinancialDataProvider>(services.GetRequiredService<IFinancialDataProvider>());
        Assert.IsType<NullMarketDataProvider>(services.GetRequiredService<IMarketDataPrefetchService>());
    }

    private static ServiceProvider BuildProvider(IEnumerable<KeyValuePair<string, string?>> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DATABASE_PROVIDER"] = "InMemory",
                ["INMEMORY_DATABASE_NAME"] = Guid.NewGuid().ToString("N")
            }.Concat(settings))
            .Build();

        return new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(configuration)
            .AddInfrastructure(configuration)
            .BuildServiceProvider();
    }
}
