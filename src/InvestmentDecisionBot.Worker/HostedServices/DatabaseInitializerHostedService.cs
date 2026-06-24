using InvestmentDecisionBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InvestmentDecisionBot.Worker.HostedServices;

public sealed class DatabaseInitializerHostedService(IServiceProvider services, ILogger<DatabaseInitializerHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        if (db.Database.IsRelational())
        {
            await db.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("Relational database migration completed.");
            return;
        }

        await db.Database.EnsureCreatedAsync(cancellationToken);
        logger.LogInformation("Non-relational debug database initialized.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
