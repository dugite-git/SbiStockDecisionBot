using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Application.DTOs;
using InvestmentDecisionBot.Domain.Entities;
using InvestmentDecisionBot.Domain.Enums;

namespace InvestmentDecisionBot.Infrastructure.Providers;

public sealed class NullMarketDataProvider : IMarketDataProvider
{
    public Task<MarketPriceResult> GetLatestPriceAsync(Security security, CancellationToken cancellationToken) =>
        Task.FromResult(new MarketPriceResult(null, security.Currency, true, true, "Market data provider is disabled."));
}

public sealed class NullNewsProvider : INewsProvider
{
    public Task<IReadOnlyList<NewsItem>> GetNewsAsync(Security security, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NewsItem>>(Array.Empty<NewsItem>());
}

public sealed class NullFinancialDataProvider : IFinancialDataProvider
{
    public Task<FinancialDataResult> GetFinancialDataAsync(Security security, CancellationToken cancellationToken) =>
        Task.FromResult(new FinancialDataResult(false));
}

public sealed class NullExchangeRateProvider : IExchangeRateProvider
{
    public Task<decimal?> GetRateAsync(string fromCurrency, string toCurrency, CancellationToken cancellationToken) =>
        Task.FromResult<decimal?>(null);
}

public sealed class DisabledAiAnalysisClient : IAiAnalysisClient
{
    public Task<AiAnalysisResultDto> AnalyzeAsync(AiAnalysisRequestDto request, CancellationToken cancellationToken) =>
        Task.FromResult(new AiAnalysisResultDto(false, null, null, 0m, "", Array.Empty<string>(), Array.Empty<string>(), null, "AI analysis is disabled or not configured."));
}

public sealed class RuleOnlyDiscordPublisher : IDiscordReportPublisher
{
    public Task<DiscordPostResult> PostReportAsync(string content, CancellationToken cancellationToken) =>
        Task.FromResult(new DiscordPostResult(false, null, "Discord publisher is not connected in this process."));
}
