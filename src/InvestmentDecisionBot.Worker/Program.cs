using InvestmentDecisionBot.Application;
using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Infrastructure;
using InvestmentDecisionBot.Presentation.Discord;
using InvestmentDecisionBot.Worker.Configuration;
using InvestmentDecisionBot.Worker.HostedServices;
using InvestmentDecisionBot.Worker.Scheduling;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddDotEnvFiles(Directory.GetCurrentDirectory(), builder.Environment.ContentRootPath);
builder.Configuration.AddEnvironmentVariables();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddDiscordPresentation(builder.Configuration);
builder.Services.AddSingleton<IReportRunCoordinator, ReportRunCoordinator>();
builder.Services.AddHostedService<DatabaseInitializerHostedService>();
builder.Services.AddHostedService<DailyReportSchedulerHostedService>();

await builder.Build().RunAsync();
