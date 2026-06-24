using InvestmentDecisionBot.Application;
using InvestmentDecisionBot.Infrastructure;
using InvestmentDecisionBot.Worker.Configuration;
using InvestmentDecisionBot.Worker.HostedServices;
using InvestmentDecisionBot.Worker.Scheduling;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddDotEnvFiles(Directory.GetCurrentDirectory(), builder.Environment.ContentRootPath);
builder.Configuration.AddEnvironmentVariables();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<ReportRunCoordinator>();
builder.Services.AddHostedService<DatabaseInitializerHostedService>();
builder.Services.AddHostedService<DiscordBotHostedService>();
builder.Services.AddHostedService<DailyReportSchedulerHostedService>();

await builder.Build().RunAsync();
