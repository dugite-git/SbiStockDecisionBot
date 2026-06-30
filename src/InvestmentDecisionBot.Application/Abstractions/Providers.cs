using InvestmentDecisionBot.Application.DTOs;
using InvestmentDecisionBot.Domain.Entities;

namespace InvestmentDecisionBot.Application.Abstractions;

public interface IMarketDataProvider
{
    Task<MarketPriceResult> GetLatestPriceAsync(Security security, CancellationToken cancellationToken);
}

public interface ICachedMarketDataProvider
{
    Task<IReadOnlyList<DailyPriceBar>> GetCachedDailyPricesAsync(Security security, CancellationToken cancellationToken);
    Task<IReadOnlyList<NewsSentimentData>> GetCachedNewsAsync(Security security, CancellationToken cancellationToken);
    Task<decimal?> GetCachedExchangeRateAsync(string fromCurrency, string toCurrency, CancellationToken cancellationToken);
}

public interface INewsProvider
{
    Task<IReadOnlyList<NewsItem>> GetNewsAsync(Security security, CancellationToken cancellationToken);
}

public interface IFinancialDataProvider
{
    Task<FinancialDataResult> GetFinancialDataAsync(Security security, CancellationToken cancellationToken);
}

public interface IExchangeRateProvider
{
    Task<decimal?> GetRateAsync(string fromCurrency, string toCurrency, CancellationToken cancellationToken);
}

public interface IAiAnalysisClient
{
    Task<AiAnalysisResultDto> AnalyzeAsync(AiAnalysisRequestDto request, CancellationToken cancellationToken);
}
