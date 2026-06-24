using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Application.DTOs;
using InvestmentDecisionBot.Domain.Entities;
using InvestmentDecisionBot.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace InvestmentDecisionBot.Application.Importing;

public sealed class ImportService(ISbiCsvParser parser, IBotDbContext db, ISystemLogService logs) : IImportService
{
    public async Task<SbiImportResult> ImportSbiCsvAsync(Stream csvStream, string? fileName, CancellationToken cancellationToken)
    {
        SbiCsvParseResult parsed;
        try
        {
            parsed = await parser.ParseAsync(csvStream, fileName, cancellationToken);
        }
        catch (Exception ex)
        {
            await logs.LogAsync("Error", "Import", "SBI CSV import failed before DB update.", ex, cancellationToken);
            return new SbiImportResult(false, $"CSV import failed: {ex.Message}", 0, 0, 0, 0, 0, "");
        }

        if (parsed.Holdings.Count == 0)
        {
            await logs.LogAsync("Warning", "Import", "SBI CSV contained no stock holdings.", null, cancellationToken);
            return new SbiImportResult(false, "CSVに株式の取り込み対象がありません。DBは更新していません。", 0, 0, 0, 0, 0, parsed.EncodingName);
        }

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var symbols = parsed.Holdings.Select(h => NormalizeSymbol(h.Symbol)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingSecurities = await db.Securities
            .Where(s => s.SecurityType == SecurityType.Stock && symbols.Contains(s.Symbol))
            .ToDictionaryAsync(s => s.Symbol, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var created = 0;
        var updated = 0;

        foreach (var imported in parsed.Holdings)
        {
            var symbol = NormalizeSymbol(imported.Symbol);
            if (!existingSecurities.TryGetValue(symbol, out var security))
            {
                security = new Security
                {
                    Symbol = symbol,
                    Name = imported.Name,
                    SecurityType = SecurityType.Stock,
                    Country = IsJapaneseSymbol(symbol) ? "JP" : null,
                    Currency = IsJapaneseSymbol(symbol) ? "JPY" : null,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.Securities.Add(security);
                existingSecurities[symbol] = security;
                created++;
            }
            else
            {
                security.Name = string.IsNullOrWhiteSpace(imported.Name) ? security.Name : imported.Name;
                security.UpdatedAt = now;
            }

            var holding = await db.Holdings
                .Include(h => h.Security)
                .FirstOrDefaultAsync(h => h.Security.Symbol == symbol && h.Security.SecurityType == SecurityType.Stock, cancellationToken);

            if (holding is null)
            {
                holding = new Holding { Security = security, CreatedAt = now };
                db.Holdings.Add(holding);
            }
            else
            {
                updated++;
            }

            holding.Quantity = imported.Quantity;
            holding.PendingSellQuantity = imported.PendingSellQuantity;
            holding.AverageAcquisitionPrice = imported.AverageAcquisitionPrice;
            holding.AcquisitionAmount = imported.AcquisitionAmount;
            holding.ImportedCurrentPrice = imported.ImportedCurrentPrice;
            holding.ImportedMarketValue = imported.ImportedMarketValue;
            holding.ImportedUnrealizedProfitLoss = imported.ImportedUnrealizedProfitLoss;
            holding.ImportedAt = now;
            holding.IsActive = true;
            holding.UpdatedAt = now;

            db.HoldingSnapshots.Add(new HoldingSnapshot
            {
                Security = security,
                Quantity = imported.Quantity,
                PendingSellQuantity = imported.PendingSellQuantity,
                AverageAcquisitionPrice = imported.AverageAcquisitionPrice,
                AcquisitionAmount = imported.AcquisitionAmount,
                ImportedCurrentPrice = imported.ImportedCurrentPrice,
                ImportedMarketValue = imported.ImportedMarketValue,
                ImportedUnrealizedProfitLoss = imported.ImportedUnrealizedProfitLoss,
                SnapshotDate = today,
                SourceCsvFileName = fileName,
                CreatedAt = now
            });
        }

        var activeHoldings = await db.Holdings.Include(h => h.Security)
            .Where(h => h.IsActive && h.Security.SecurityType == SecurityType.Stock)
            .ToListAsync(cancellationToken);

        var soldCount = 0;
        var watchAdded = 0;
        foreach (var holding in activeHoldings.Where(h => !symbols.Contains(h.Security.Symbol)))
        {
            holding.IsActive = false;
            holding.UpdatedAt = now;
            soldCount++;

            db.SoldEvents.Add(new SoldEvent
            {
                Security = holding.Security,
                DetectedAt = now,
                PreviousQuantity = holding.Quantity,
                PreviousAverageAcquisitionPrice = holding.AverageAcquisitionPrice,
                PreviousImportedCurrentPrice = holding.ImportedCurrentPrice,
                PreviousImportedMarketValue = holding.ImportedMarketValue,
                PreviousImportedUnrealizedProfitLoss = holding.ImportedUnrealizedProfitLoss,
                CreatedAt = now
            });

            var existingWatch = await db.WatchlistItems
                .FirstOrDefaultAsync(w => w.SecurityId == holding.SecurityId && w.IsActive, cancellationToken);
            if (existingWatch is null)
            {
                db.WatchlistItems.Add(new WatchlistItem
                {
                    Security = holding.Security,
                    Source = WatchlistSource.SoldAutomatically,
                    IsActive = true,
                    AddedAt = now,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                watchAdded++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new SbiImportResult(true, "CSVインポート完了", parsed.Holdings.Count, created, updated, soldCount, watchAdded, parsed.EncodingName);
    }

    private static string NormalizeSymbol(string symbol) => symbol.Trim().ToUpperInvariant();

    private static bool IsJapaneseSymbol(string symbol) => symbol.Length == 4 && symbol.All(char.IsDigit);
}
