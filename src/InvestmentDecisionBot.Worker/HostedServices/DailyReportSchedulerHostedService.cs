using InvestmentDecisionBot.Application.Abstractions;

namespace InvestmentDecisionBot.Worker.HostedServices;

public sealed class DailyReportSchedulerHostedService(
    IServiceProvider services,
    IConfiguration configuration,
    IReportRunCoordinator coordinator,
    ILogger<DailyReportSchedulerHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = GetDelayUntilNextRun();
            logger.LogInformation("Next daily report run in {Delay}.", delay);
            await Task.Delay(delay, stoppingToken);

            await coordinator.TryRunAsync(async ct =>
            {
                using var scope = services.CreateScope();
                var reports = scope.ServiceProvider.GetRequiredService<IReportService>();
                await reports.GenerateDailyReportAsync(postToDiscord: true, ct);
            }, stoppingToken);
        }
    }

    private TimeSpan GetDelayUntilNextRun()
    {
        var timeZoneId = configuration["TIME_ZONE"] ?? "Asia/Tokyo";
        var reportTimeText = configuration["REPORT_TIME"] ?? "08:00";
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);
        if (!TimeOnly.TryParse(reportTimeText, out var reportTime))
        {
            reportTime = new TimeOnly(8, 0);
        }

        var next = new DateTimeOffset(now.Date + reportTime.ToTimeSpan(), now.Offset);
        if (next <= now)
        {
            next = next.AddDays(1);
        }

        return next - now;
    }
}
