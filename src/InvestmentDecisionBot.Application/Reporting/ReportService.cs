using System.Text;
using System.Text.Json;
using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Application.DTOs;
using InvestmentDecisionBot.Domain.Entities;
using InvestmentDecisionBot.Domain.Enums;

namespace InvestmentDecisionBot.Application.Reporting;

public sealed class ReportService(
    IHoldingRepository holdings,
    IWatchlistRepository watchlist,
    IMarketPriceSnapshotRepository marketPriceSnapshots,
    IExternalApiCacheRepository externalApiCache,
    IAnalysisResultRepository analysisResults,
    IImportBatchRepository importBatches,
    IAnalysisRunRepository analysisRuns,
    IDailyReportRepository dailyReports,
    IUnitOfWork unitOfWork,
    IScoreCalculator scoreCalculator,
    IBotDecisionResolver decisionResolver,
    IMarketDataProvider marketData,
    ICachedMarketDataProvider cachedMarketData,
    IFinancialDataProvider financialData,
    IDiscordReportPublisher publisher,
    ISystemLogService logs) : IReportService
{
    public async Task<ReportResult> GenerateDailyReportAsync(bool postToDiscord, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var latestImportBatch = await importBatches.FindLatestSucceededAsync(cancellationToken);
        var analysisRun = new AnalysisRun
        {
            AnalysisDate = today,
            StartedAt = now,
            Trigger = "Daily",
            ImportBatch = latestImportBatch,
            Succeeded = false,
            CreatedAt = now
        };
        analysisRuns.Add(analysisRun);

        try
        {
            var activeHoldings = (await holdings.ListActiveWithSecurityAsync(cancellationToken))
                .Where(h => h.Security.IsJapaneseStock())
                .ToList();
            var activeWatchlist = (await watchlist.ListActiveWithSecurityAsync(cancellationToken))
                .Where(w => w.Security.IsJapaneseStock())
                .ToList();
            var holdingIds = activeHoldings.Select(h => h.SecurityId).ToHashSet();

            var analyses = new List<(AnalysisInput Input, ScoreResult Score, DecisionResult Decision)>();
            var totalPortfolioMarketValue = activeHoldings
                .Select(holding => holding.ImportedMarketValue ?? holding.ImportedCurrentPrice * holding.Quantity)
                .Where(value => value is > 0m)
                .Sum(value => value!.Value);

            foreach (var holding in activeHoldings)
            {
                var input = await BuildHoldingInputAsync(holding, totalPortfolioMarketValue, cancellationToken);
                analyses.Add(await AnalyzeAsync(input, analysisRun, today, cancellationToken));
            }

            foreach (var item in activeWatchlist.Where(w => !holdingIds.Contains(w.SecurityId)))
            {
                var input = await BuildWatchlistInputAsync(item, cancellationToken);
                analyses.Add(await AnalyzeAsync(input, analysisRun, today, cancellationToken));
            }

            var content = BuildReportContent(analyses);
            var summaryItems = BuildSummaryItems(analyses);
            var importantItems = summaryItems
                .Where(item => IsImportant(item.Decision))
                .OrderBy(item => item.Symbol)
                .Take(5)
                .ToList();
            var decisionCounts = BuildDecisionCounts(summaryItems);
            var missingDataCategories = summaryItems
                .SelectMany(item => item.MissingData)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var dailyReport = new DailyReport
            {
                AnalysisRun = analysisRun,
                ReportDate = today,
                Content = content,
                GeneratedAt = now,
                PostedToDiscord = false,
                CreatedAt = now
            };
            dailyReports.Add(dailyReport);

            DiscordPostResult? postResult = null;
            if (postToDiscord)
            {
                postResult = await publisher.PostReportAsync(content, cancellationToken);
                dailyReport.PostedToDiscord = postResult.Succeeded;
                dailyReport.DiscordMessageId = postResult.MessageId;
                dailyReport.PostedAt = postResult.Succeeded ? DateTimeOffset.UtcNow : null;
                if (!postResult.Succeeded)
                {
                    await logs.LogAsync("Error", "Discord", postResult.ErrorMessage ?? "Discord投稿に失敗しました。", null, cancellationToken);
                }
            }

            analysisRun.Succeeded = true;
            analysisRun.FinishedAt = DateTimeOffset.UtcNow;
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new ReportResult(
                postResult?.Succeeded ?? true,
                content,
                analyses.Count,
                postResult?.MessageId,
                postResult?.ErrorMessage,
                analysisRun.Id,
                latestImportBatch?.Id,
                latestImportBatch?.SourceCsvFileName,
                latestImportBatch?.ImportedAt,
                importantItems,
                summaryItems,
                decisionCounts,
                missingDataCategories);
        }
        catch (Exception ex)
        {
            analysisRun.Succeeded = false;
            analysisRun.FinishedAt = DateTimeOffset.UtcNow;
            analysisRun.ErrorMessage = ex.Message;
            try
            {
                // If report generation fails after some analysis rows or price snapshots have been added,
                // those partial rows may also be saved with the failed AnalysisRun.
                // This is intentional: partial results are useful for diagnostics and should be interpreted
                // together with AnalysisRun.Succeeded == false.
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                // Preserve the original failure when the best-effort failure log cannot be saved.
            }

            throw;
        }
    }

    private static string BuildReportContent(IReadOnlyList<(AnalysisInput Input, ScoreResult Score, DecisionResult Decision)> analyses)
    {
        var lines = new StringBuilder();
        lines.AppendLine("本日の投資判断レポート");
        lines.AppendLine();
        lines.AppendLine("重要な判断");

        foreach (var entry in analyses.OrderByDescending(a => IsImportant(a.Decision.Decision)).ThenBy(a => a.Input.Symbol).Take(10))
        {
            var rate = entry.Score.UnrealizedProfitLossRate is null ? "N/A" : $"{entry.Score.UnrealizedProfitLossRate:+0.##;-0.##;0}%";
            lines.AppendLine($"- {entry.Input.Symbol} {entry.Input.Name}: {entry.Decision.Decision} / {rate}");
        }

        lines.AppendLine();
        lines.AppendLine("保有銘柄");
        foreach (var entry in analyses.Where(a => a.Input.TargetType == TargetType.Holding))
        {
            AppendAnalysis(lines, entry);
        }

        lines.AppendLine();
        lines.AppendLine("ウォッチリスト");
        foreach (var entry in analyses.Where(a => a.Input.TargetType == TargetType.Watchlist))
        {
            AppendAnalysis(lines, entry);
        }

        var missing = analyses.SelectMany(a => a.Score.MissingData).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        lines.AppendLine();
        lines.AppendLine("注意");
        lines.AppendLine("- このレポートは投資判断の参考情報です。売買を推奨または強制するものではありません。");
        lines.AppendLine("- SBI証券へのログイン、注文発行、自動売買、資産管理は行いません。");
        if (missing.Count > 0)
        {
            lines.AppendLine($"- 不足データ: {string.Join(", ", missing)} は中立値またはフォールバックで扱いました。");
        }

        return lines.ToString().TrimEnd();
    }

    private static IReadOnlyList<ReportDecisionSummaryItem> BuildSummaryItems(
        IReadOnlyList<(AnalysisInput Input, ScoreResult Score, DecisionResult Decision)> analyses) =>
        analyses
            .OrderByDescending(entry => IsImportant(entry.Decision.Decision))
            .ThenBy(entry => entry.Input.Symbol)
            .Select(entry => new ReportDecisionSummaryItem(
                entry.Input.Symbol,
                entry.Input.Name,
                entry.Input.TargetType,
                entry.Decision.Decision,
                entry.Score.TotalScore,
                entry.Score.UnrealizedProfitLossRate,
                entry.Decision.Confidence,
                entry.Decision.Reason,
                entry.Score.FundamentalScore,
                entry.Score.QualityScore,
                entry.Score.MomentumScore,
                entry.Score.NewsScore,
                entry.Score.PositionRiskScore,
                entry.Score.MissingData,
                entry.Score.Warnings ?? Array.Empty<string>()))
            .ToList();

    private static ReportDecisionCounts BuildDecisionCounts(IReadOnlyList<ReportDecisionSummaryItem> items) =>
        new(
            items.Count(item => item.Decision == BotDecision.TakeProfit),
            items.Count(item => item.Decision == BotDecision.PartialTakeProfit),
            items.Count(item => item.Decision == BotDecision.StopLoss),
            items.Count(item => item.Decision == BotDecision.PartialStopLoss),
            items.Count(item => item.Decision == BotDecision.NewBuy),
            items.Count(item => item.Decision == BotDecision.Hold),
            items.Count(item => IsImportant(item.Decision)));

    private async Task<AnalysisInput> BuildHoldingInputAsync(Holding holding, decimal totalPortfolioMarketValue, CancellationToken cancellationToken)
    {
        var latestPrice = await FetchLatestPriceAsync(holding.Security, cancellationToken);
        var missingData = new List<string>();
        if (latestPrice.Price is null || latestPrice.UsedFallback)
        {
            missingData.Add("market");
        }

        var currentPrice = holding.ImportedCurrentPrice ?? latestPrice.Price;
        var marketValue = currentPrice is null ? holding.ImportedMarketValue : decimal.Round(currentPrice.Value * holding.Quantity, 4);
        var unrealizedProfitLoss = marketValue is null
            ? holding.ImportedUnrealizedProfitLoss
            : decimal.Round(marketValue.Value - holding.AverageAcquisitionPrice * holding.Quantity, 4);
        var dailyPrices = await cachedMarketData.GetCachedDailyPricesAsync(holding.Security, cancellationToken);
        var news = await cachedMarketData.GetCachedNewsAsync(holding.Security, cancellationToken);
        var financial = await financialData.GetFinancialDataAsync(holding.Security, cancellationToken);
        var cacheWarnings = new List<string>();
        if (dailyPrices.Count == 0) missingData.Add("daily");
        if (news.Count == 0 && !await HasSuccessfulNewsFetchAsync(holding.Security, cancellationToken))
        {
            missingData.Add("news");
        }
        else if (news.Count == 0)
        {
            cacheWarnings.Add("ニュース取得は成功しましたが、該当記事が0件だったためNewsScoreは中立値で評価しています。");
        }

        if (!financial.HasData) missingData.Add("financial");

        return new AnalysisInput(
            holding.SecurityId,
            holding.Security.Symbol,
            holding.Security.Name,
            TargetType.Holding,
            holding.Quantity,
            holding.AverageAcquisitionPrice,
            currentPrice,
            marketValue,
            unrealizedProfitLoss,
            missingData,
            dailyPrices,
            news,
            financial.Snapshot,
            totalPortfolioMarketValue,
            holding.Security.Currency,
            cacheWarnings);
    }

    private async Task<AnalysisInput> BuildWatchlistInputAsync(WatchlistItem item, CancellationToken cancellationToken)
    {
        var latestPrice = await FetchLatestPriceAsync(item.Security, cancellationToken);
        var missingData = new List<string>();
        if (latestPrice.Price is null || latestPrice.UsedFallback)
        {
            missingData.Add("market");
        }

        var dailyPrices = await cachedMarketData.GetCachedDailyPricesAsync(item.Security, cancellationToken);
        var news = await cachedMarketData.GetCachedNewsAsync(item.Security, cancellationToken);
        var financial = await financialData.GetFinancialDataAsync(item.Security, cancellationToken);
        var cacheWarnings = new List<string>();
        if (dailyPrices.Count == 0) missingData.Add("daily");
        if (news.Count == 0 && !await HasSuccessfulNewsFetchAsync(item.Security, cancellationToken))
        {
            missingData.Add("news");
        }
        else if (news.Count == 0)
        {
            cacheWarnings.Add("ニュース取得は成功しましたが、該当記事が0件だったためNewsScoreは中立値で評価しています。");
        }

        if (!financial.HasData) missingData.Add("financial");

        return new AnalysisInput(
            item.SecurityId,
            item.Security.Symbol,
            item.Security.Name,
            TargetType.Watchlist,
            null,
            null,
            latestPrice.Price,
            null,
            null,
            missingData,
            dailyPrices,
            news,
            financial.Snapshot,
            null,
            item.Security.Currency,
            cacheWarnings);
    }

    private async Task<bool> HasSuccessfulNewsFetchAsync(Security security, CancellationToken cancellationToken) =>
        await externalApiCache.HasSuccessfulNewsFetchAsync(security.Symbol, cancellationToken);

    private async Task<MarketPriceResult> FetchLatestPriceAsync(Security security, CancellationToken cancellationToken)
    {
        var cached = await marketPriceSnapshots.FindReusableTodayAsync(security.Id, cancellationToken);
        if (cached is not null)
        {
            return new MarketPriceResult(cached.Price, cached.Currency, false, cached.IsStale, null);
        }

        var result = await marketData.GetLatestPriceAsync(security, cancellationToken);
        if (result.Price is null)
        {
            return result;
        }

        if (await marketPriceSnapshots.HasReusableTodayAsync(security.Id, cancellationToken))
        {
            return result;
        }

        marketPriceSnapshots.Add(new MarketPriceSnapshot
        {
            SecurityId = security.Id,
            Price = result.Price,
            Currency = result.Currency ?? security.Currency,
            FetchedAt = DateTimeOffset.UtcNow,
            DataSource = marketData.GetType().Name,
            IsStale = result.IsStale,
            UsedFallback = result.UsedFallback,
            ErrorMessage = result.ErrorMessage,
            CreatedAt = DateTimeOffset.UtcNow
        });

        return result;
    }

    private async Task<(AnalysisInput Input, ScoreResult Score, DecisionResult Decision)> AnalyzeAsync(
        AnalysisInput input,
        AnalysisRun analysisRun,
        DateOnly analysisDate,
        CancellationToken cancellationToken)
    {
        var score = scoreCalculator.Calculate(input);
        var decision = decisionResolver.Resolve(input, score);

        var analysis = new AnalysisResult
        {
            AnalysisRun = analysisRun,
            SecurityId = input.SecurityId,
            AnalysisDate = analysisDate,
            TargetType = input.TargetType,
            FundamentalScore = score.FundamentalScore,
            QualityScore = score.QualityScore,
            MomentumScore = score.MomentumScore,
            NewsScore = score.NewsScore,
            PositionRiskScore = score.PositionRiskScore,
            TotalScore = score.TotalScore,
            BotDecision = decision.Decision,
            SellReasonType = decision.SellReasonType,
            Confidence = decision.Confidence,
            Reason = BuildReason(decision, score),
            MissingData = string.Join(",", score.MissingData),
            ScoreDetailsJson = BuildScoreDetailsJson(score, decision),
            InputDataSummaryJson = BuildInputDataSummaryJson(input),
            DecisionConflict = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        analysisResults.Add(analysis);
        await Task.CompletedTask;

        return (input, score, decision);
    }

    private static void AppendAnalysis(StringBuilder lines, (AnalysisInput Input, ScoreResult Score, DecisionResult Decision) entry)
    {
        var rate = entry.Score.UnrealizedProfitLossRate is null ? "N/A" : $"{entry.Score.UnrealizedProfitLossRate:+0.##;-0.##;0}%";
        lines.AppendLine($"- {entry.Input.Symbol} {entry.Input.Name}: {entry.Decision.Decision} / スコア {entry.Score.TotalScore:0.##} / 含み損益率 {rate}");
        lines.AppendLine($"  理由: {entry.Decision.Reason}");

        lines.AppendLine($"  SubScores: {FormatSubScores(entry.Score)}");

        foreach (var warning in (entry.Score.Warnings ?? Array.Empty<string>()).Take(3))
        {
            lines.AppendLine($"  警告: {warning}");
        }
    }

    private static string FormatSubScores(ScoreResult score) =>
        $"F {score.FundamentalScore:0.##}, Q {score.QualityScore:0.##}, M {score.MomentumScore:0.##}, N {score.NewsScore:0.##}, R {score.PositionRiskScore:0.##}";

    private static string BuildReason(DecisionResult decision, ScoreResult score)
    {
        var details = new List<string> { decision.Reason };
        if (score.Reasons is not null && score.Reasons.Count > 0)
        {
            details.Add(string.Join(" ", score.Reasons.Take(3)));
        }

        if (score.Warnings is not null && score.Warnings.Count > 0)
        {
            details.Add("警告: " + string.Join(" ", score.Warnings.Take(3)));
        }

        return string.Join(" ", details);
    }

    private static string BuildScoreDetailsJson(ScoreResult score, DecisionResult decision) =>
        JsonSerializer.Serialize(new
        {
            fundamental = score.Fundamental,
            quality = score.Quality,
            momentum = score.Momentum,
            news = score.News,
            positionRisk = score.PositionRisk,
            totalScore = score.TotalScore,
            unrealizedProfitLossRate = score.UnrealizedProfitLossRate,
            missingData = score.MissingData,
            reasons = score.Reasons,
            warnings = score.Warnings,
            decision = new
            {
                decision = decision.Decision,
                sellReasonType = decision.SellReasonType,
                confidence = decision.Confidence,
                reason = decision.Reason
            }
        });

    private static string BuildInputDataSummaryJson(AnalysisInput input) =>
        JsonSerializer.Serialize(new
        {
            securityId = input.SecurityId,
            symbol = input.Symbol,
            targetType = input.TargetType,
            hasCurrentPrice = input.CurrentPrice is not null,
            hasMarketValue = input.MarketValue is not null,
            dailyPriceCount = input.DailyPrices?.Count ?? 0,
            newsCount = input.News?.Count ?? 0,
            hasFinancialData = input.FinancialSnapshot is not null,
            missingData = input.MissingData,
            currency = input.Currency
        });

    private static bool IsImportant(BotDecision decision) =>
        decision is BotDecision.TakeProfit or BotDecision.PartialTakeProfit or BotDecision.StopLoss or BotDecision.PartialStopLoss or BotDecision.NewBuy;
}
