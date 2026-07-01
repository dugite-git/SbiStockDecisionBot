using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Application.DTOs;
using InvestmentDecisionBot.Domain.Entities;
using InvestmentDecisionBot.Domain.Enums;

namespace InvestmentDecisionBot.Application.Importing;

public sealed class ImportService(
    ISbiCsvParser parser,
    ISecurityRepository securities,
    IHoldingRepository holdings,
    IHoldingSnapshotRepository holdingSnapshots,
    IImportBatchRepository importBatches,
    IWatchlistRepository watchlist,
    ISoldEventRepository soldEvents,
    IUnitOfWork unitOfWork,
    ISystemLogService logs) : IImportService
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
            return new SbiImportResult(false, $"CSV取り込みに失敗しました: {ex.Message}", 0, 0, 0, 0, 0, "");
        }

        if (parsed.Holdings.Count == 0)
        {
            await logs.LogAsync("Warning", "Import", "SBI CSV contained no stock holdings.", null, cancellationToken);
            return new SbiImportResult(false, "CSVに取り込み対象の株式保有データがありませんでした。DBは更新していません。", 0, 0, 0, 0, 0, parsed.EncodingName);
        }

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var supportedHoldings = parsed.Holdings
            .Select(holding => holding with { Symbol = NormalizeSymbol(holding.Symbol) })
            .Where(holding => IsJapaneseSymbol(holding.Symbol))
            .ToList();
        var skippedUnsupported = parsed.Holdings.Count - supportedHoldings.Count;
        if (supportedHoldings.Count == 0)
        {
            await logs.LogAsync("Warning", "Import", "SBI CSV contained no supported Japanese stock holdings.", null, cancellationToken);
            return new SbiImportResult(false, $"CSVに対象の日本株4桁コードがありませんでした。スキップ: {skippedUnsupported}件。DBは更新していません。", 0, 0, 0, 0, 0, parsed.EncodingName);
        }

        var symbols = supportedHoldings.Select(h => h.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingSecurities = (await securities.FindBySymbolsAsync(SecurityType.Stock, symbols, cancellationToken))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        var importBatch = new ImportBatch
        {
            SourceCsvFileName = fileName,
            EncodingName = parsed.EncodingName,
            ImportedAt = now,
            ImportedCount = supportedHoldings.Count,
            SkippedCount = skippedUnsupported,
            Succeeded = true,
            CreatedAt = now
        };
        importBatches.Add(importBatch);

        var created = 0;
        var updated = 0;

        foreach (var imported in supportedHoldings)
        {
            var symbol = imported.Symbol;
            if (!existingSecurities.TryGetValue(symbol, out var security))
            {
                security = new Security
                {
                    Symbol = symbol,
                    Name = imported.Name,
                    SecurityType = SecurityType.Stock,
                    Country = "JP",
                    Currency = "JPY",
                    CreatedAt = now,
                    UpdatedAt = now
                };
                securities.Add(security);
                existingSecurities[symbol] = security;
                created++;
            }
            else
            {
                security.Name = string.IsNullOrWhiteSpace(imported.Name) ? security.Name : imported.Name;
                security.UpdatedAt = now;
            }

            var holding = await holdings.FindByStockSymbolWithSecurityAsync(symbol, cancellationToken);

            if (holding is null)
            {
                holding = new Holding { Security = security, CreatedAt = now };
                holdings.Add(holding);
            }
            else
            {
                updated++;
            }

            holding.UpdateFromImportedData(
                imported.Quantity,
                imported.PendingSellQuantity,
                imported.AverageAcquisitionPrice,
                imported.AcquisitionAmount,
                imported.ImportedCurrentPrice,
                imported.ImportedMarketValue,
                imported.ImportedUnrealizedProfitLoss,
                now);

            holdingSnapshots.Add(new HoldingSnapshot
            {
                Security = security,
                ImportBatch = importBatch,
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

        var activeHoldings = (await holdings.ListActiveWithSecurityAsync(cancellationToken))
            .Where(holding => holding.Security.SecurityType == SecurityType.Stock)
            .ToList();

        var soldCount = 0;
        var watchAdded = 0;
        foreach (var holding in activeHoldings.Where(h => !symbols.Contains(h.Security.Symbol)))
        {
            holding.MarkAsSold(now);
            soldCount++;

            soldEvents.Add(new SoldEvent
            {
                Security = holding.Security,
                ImportBatch = importBatch,
                DetectedAt = now,
                PreviousQuantity = holding.Quantity,
                PreviousAverageAcquisitionPrice = holding.AverageAcquisitionPrice,
                PreviousImportedCurrentPrice = holding.ImportedCurrentPrice,
                PreviousImportedMarketValue = holding.ImportedMarketValue,
                PreviousImportedUnrealizedProfitLoss = holding.ImportedUnrealizedProfitLoss,
                CreatedAt = now
            });

            var existingWatch = await watchlist.FindActiveBySecurityIdAsync(holding.SecurityId, cancellationToken);
            if (existingWatch is null)
            {
                watchlist.Add(new WatchlistItem
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

        importBatch.CreatedCount = created;
        importBatch.UpdatedCount = updated;
        importBatch.SoldCount = soldCount;
        importBatch.WatchAddedCount = watchAdded;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        var message = skippedUnsupported == 0
            ? "CSV取り込みが完了しました。"
            : $"CSV取り込みが完了しました。対象外コードを{skippedUnsupported}件スキップしました。";
        return new SbiImportResult(true, message, supportedHoldings.Count, created, updated, soldCount, watchAdded, parsed.EncodingName);
    }

    private static string NormalizeSymbol(string symbol) => symbol.Trim().ToUpperInvariant();

    private static bool IsJapaneseSymbol(string symbol) => symbol.Length == 4 && symbol.All(char.IsDigit);
}
