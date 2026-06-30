using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Application.DTOs;
using InvestmentDecisionBot.Domain.Entities;
using InvestmentDecisionBot.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace InvestmentDecisionBot.Application.Watchlist;

public sealed class WatchlistService(IBotDbContext db) : IWatchlistService
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

        var security = await db.Securities.FirstOrDefaultAsync(s => s.Symbol == symbol && s.SecurityType == SecurityType.Stock, cancellationToken);
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
            db.Securities.Add(security);
        }

        var existing = await db.WatchlistItems.FirstOrDefaultAsync(w => w.SecurityId == security.Id && w.IsActive, cancellationToken);
        if (existing is not null)
        {
            return new WatchlistMutationResult(true, $"{symbol} はすでにウォッチリストに登録されています。");
        }

        db.WatchlistItems.Add(new WatchlistItem
        {
            Security = security,
            Source = WatchlistSource.Manual,
            IsActive = true,
            AddedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
        return new WatchlistMutationResult(true, $"{symbol} をウォッチリストに追加しました。");
    }

    public async Task<WatchlistMutationResult> RemoveAsync(string symbol, CancellationToken cancellationToken)
    {
        symbol = NormalizeSymbol(symbol);
        var item = await db.WatchlistItems
            .Include(w => w.Security)
            .FirstOrDefaultAsync(w => w.Security.Symbol == symbol && w.IsActive, cancellationToken);

        if (item is null)
        {
            return new WatchlistMutationResult(false, $"{symbol} はウォッチリストに登録されていません。");
        }

        item.IsActive = false;
        item.RemovedAt = DateTimeOffset.UtcNow;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return new WatchlistMutationResult(true, $"{symbol} をウォッチリストから外しました。保有中の場合は分析対象として残ります。");
    }

    public async Task<IReadOnlyList<WatchlistItemDto>> ListAsync(CancellationToken cancellationToken)
    {
        var activeHoldingSecurityIds = await db.Holdings.Where(h => h.IsActive).Select(h => h.SecurityId).ToListAsync(cancellationToken);
        return await db.WatchlistItems
            .Include(w => w.Security)
            .Where(w => w.IsActive)
            .OrderBy(w => w.Security.Symbol)
            .Select(w => new WatchlistItemDto(w.Security.Symbol, w.Security.Name, w.Source, activeHoldingSecurityIds.Contains(w.SecurityId)))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WatchTargetDto>> ListTargetsAsync(CancellationToken cancellationToken)
    {
        var activeHoldingSecurityIds = await db.Holdings
            .Where(holding => holding.IsActive)
            .Select(holding => holding.SecurityId)
            .ToListAsync(cancellationToken);
        var holdingIds = activeHoldingSecurityIds.ToHashSet();

        var activeWatchlistItems = await db.WatchlistItems
            .Where(item => item.IsActive)
            .Select(item => new { item.SecurityId, item.Source })
            .ToListAsync(cancellationToken);
        var watchlistSources = activeWatchlistItems
            .GroupBy(item => item.SecurityId)
            .ToDictionary(group => group.Key, group => group.First().Source);

        var targetIds = holdingIds
            .Concat(watchlistSources.Keys)
            .Distinct()
            .ToList();

        if (targetIds.Count == 0)
        {
            return [];
        }

        var securities = await db.Securities
            .Where(security => targetIds.Contains(security.Id))
            .OrderBy(security => security.Symbol)
            .ToListAsync(cancellationToken);

        return securities
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
