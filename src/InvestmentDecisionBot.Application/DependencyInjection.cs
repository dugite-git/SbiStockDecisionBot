using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Application.Importing;
using InvestmentDecisionBot.Application.Reporting;
using InvestmentDecisionBot.Application.Scoring;
using InvestmentDecisionBot.Application.Watchlist;
using Microsoft.Extensions.DependencyInjection;

namespace InvestmentDecisionBot.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IScoreCalculator, ScoreCalculator>();
        services.AddScoped<IBotDecisionResolver, BotDecisionResolver>();
        services.AddScoped<IImportService, ImportService>();
        services.AddScoped<IWatchlistService, WatchlistService>();
        services.AddScoped<IReportService, ReportService>();
        return services;
    }
}
