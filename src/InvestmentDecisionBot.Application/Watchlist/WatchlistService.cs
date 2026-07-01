using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Application.DTOs;
using InvestmentDecisionBot.Domain.Entities;
using InvestmentDecisionBot.Domain.Enums;

namespace InvestmentDecisionBot.Application.Watchlist;

public sealed class WatchlistService(
    ISecurityRepository securities,
    IHoldingRepository holdings,
    IWatchlistRepository watchlist,
    IUnitOfWork unitOfWork) : IWatchlistService
{
    public async Task<WatchlistMutationResult> AddAsync(string symbol, CancellationToken cancellationToken)
    {
        symbol = NormalizeSymbol(symbol);
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return new WatchlistMutationResult(false, "銘柄コードを指定してください。");
        }

        if (!IsJapaneseSymbol(symbol))
        {
            return new WatchlistMutationResult(false, $"{symbol} は対象外です。日本株の4桁コードのみ登録できます。");
        }

        var security = await securities.FindBySymbolAsync(SecurityType.Stock, symbol, cancellationToken);
        if (security is null)
        {
            security = new Security
            {
                Symbol = symbol,
                Name = symbol,
                SecurityType = SecurityType.Stock,
                Country = "JP",
                Currency = "JPY"
            };
            securities.Add(security);
        }

        var existing = await watchlist.FindActiveBySecurityIdAsync(security.Id, cancellationToken);
        if (existing is not null)
        {
            return new WatchlistMutationResult(true, $"{symbol} はすでにウォッチリストに登録されています。");
        }

        watchlist.Add(new WatchlistItem
        {
            Security = security,
            Source = WatchlistSource.Manual,
            IsActive = true,
            AddedAt = DateTimeOffset.UtcNow
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new WatchlistMutationResult(true, $"{symbol} をウォッチリストに追加しました。");
    }

    public async Task<WatchlistMutationResult> RemoveAsync(string symbol, CancellationToken cancellationToken)
    {
        symbol = NormalizeSymbol(symbol);
        var item = await watchlist.FindActiveBySymbolWithSecurityAsync(symbol, cancellationToken);

        if (item is null)
        {
            return new WatchlistMutationResult(false, $"{symbol} はウォッチリストに登録されていません。");
        }

        item.Deactivate(DateTimeOffset.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new WatchlistMutationResult(true, $"{symbol} をウォッチリストから外しました。保有中の場合は分析対象として残ります。");
    }

    public async Task<IReadOnlyList<WatchlistItemDto>> ListAsync(CancellationToken cancellationToken)
    {
        var activeHoldingSecurityIds = await holdings.ListActiveSecurityIdsAsync(cancellationToken);
        var items = await watchlist.ListActiveWithSecurityAsync(cancellationToken);
        return items
            .Select(item => new WatchlistItemDto(
                item.Security.Symbol,
                item.Security.Name,
                item.Source,
                activeHoldingSecurityIds.Contains(item.SecurityId)))
            .ToList();
    }

    public async Task<IReadOnlyList<WatchTargetDto>> ListTargetsAsync(CancellationToken cancellationToken)
    {
        var activeHoldingSecurityIds = await holdings.ListActiveSecurityIdsAsync(cancellationToken);
        var holdingIds = activeHoldingSecurityIds.ToHashSet();
        var watchlistSources = await watchlist.ListActiveSecuritySourcesAsync(cancellationToken);

        var targetIds = holdingIds
            .Concat(watchlistSources.Keys)
            .Distinct()
            .ToList();

        if (targetIds.Count == 0)
        {
            return [];
        }

        var targetSecurities = await securities.ListByIdsAsync(targetIds, cancellationToken);
        return targetSecurities
            .Select(security => new WatchTargetDto(
                security.Symbol,
                security.Name,
                security.SecurityType.ToString(),
                security.Market,
                security.Country,
                security.Currency,
                security.ExternalSymbol,
                security.ExternalSymbolResolutionError,
                holdingIds.Contains(security.Id),
                watchlistSources.ContainsKey(security.Id),
                watchlistSources.ContainsKey(security.Id) ? watchlistSources[security.Id] : null))
            .ToList();
    }

    private static string NormalizeSymbol(string symbol) => symbol.Trim().ToUpperInvariant();

    private static bool IsJapaneseSymbol(string symbol) => symbol.Length == 4 && symbol.All(char.IsDigit);
}
