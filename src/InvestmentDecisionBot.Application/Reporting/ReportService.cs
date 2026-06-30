using System.Text;
using System.Text.Json;
using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Application.DTOs;
using InvestmentDecisionBot.Domain.Entities;
using InvestmentDecisionBot.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace InvestmentDecisionBot.Application.Reporting;

public sealed class ReportService(
    IBotDbContext db,
    IScoreCalculator scoreCalculator,
    IBotDecisionResolver decisionResolver,
    IMarketDataProvider marketData,
    ICachedMarketDataProvider cachedMarketData,
    IAiAnalysisClient ai,
    IDiscordReportPublisher publisher,
    ISystemLogService logs) : IReportService
{
    public async Task<ReportResult> GenerateDailyReportAsync(bool postToDiscord, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var holdings = await db.Holdings.Include(h => h.Security).Where(h => h.IsActive).OrderBy(h => h.Security.Symbol).ToListAsync(cancellationToken);
        var watchlist = await db.WatchlistItems.Include(w => w.Security).Where(w => w.IsActive).OrderBy(w => w.Security.Symbol).ToListAsync(cancellationToken);
        var holdingIds = holdings.Select(h => h.SecurityId).ToHashSet();

        var analyses = new List<(AnalysisInput Input, ScoreResult Score, DecisionResult Decision, AiAnalysisResultDto Ai)>();
        var totalPortfolioMarketValue = holdings
            .Select(holding => holding.ImportedMarketValue ?? holding.ImportedCurrentPrice * holding.Quantity)
            .Where(value => value is > 0m)
            .Sum(value => value!.Value);

        foreach (var holding in holdings)
        {
            var input = await BuildHoldingInputAsync(holding, totalPortfolioMarketValue, cancellationToken);
            analyses.Add(await AnalyzeAsync(input, cancellationToken));
        }

        foreach (var item in watchlist.Where(w => !holdingIds.Contains(w.SecurityId)))
        {
            var input = await BuildWatchlistInputAsync(item, cancellationToken);
            analyses.Add(await AnalyzeAsync(input, cancellationToken));
        }

        var content = BuildReportContent(analyses);
        var dailyReport = new DailyReport
        {
            ReportDate = today,
            Content = content,
            GeneratedAt = now,
            PostedToDiscord = false,
            CreatedAt = now
        };
        db.DailyReports.Add(dailyReport);

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

        await db.SaveChangesAsync(cancellationToken);
        return new ReportResult(postResult?.Succeeded ?? true, content, analyses.Count, postResult?.MessageId, postResult?.ErrorMessage);
    }

    private static string BuildReportContent(IReadOnlyList<(AnalysisInput Input, ScoreResult Score, DecisionResult Decision, AiAnalysisResultDto Ai)> analyses)
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

    private async Task<AnalysisInput> BuildHoldingInputAsync(Holding holding, decimal totalPortfolioMarketValue, CancellationToken cancellationToken)
    {
        var latestPrice = await FetchLatestPriceAsync(holding.Security, cancellationToken);
        var missingData = new List<string>();
        if (latestPrice.Price is null || latestPrice.UsedFallback)
        {
            missingData.Add("market");
        }

        var currentPrice = latestPrice.Price ?? holding.ImportedCurrentPrice;
        var marketValue = currentPrice is null ? holding.ImportedMarketValue : decimal.Round(currentPrice.Value * holding.Quantity, 4);
        var unrealizedProfitLoss = marketValue is null
            ? holding.ImportedUnrealizedProfitLoss
            : decimal.Round(marketValue.Value - holding.AverageAcquisitionPrice * holding.Quantity, 4);
        var dailyPrices = await cachedMarketData.GetCachedDailyPricesAsync(holding.Security, cancellationToken);
        var news = await cachedMarketData.GetCachedNewsAsync(holding.Security, cancellationToken);
        if (dailyPrices.Count == 0) missingData.Add("daily");
        if (news.Count == 0) missingData.Add("news");

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
            totalPortfolioMarketValue,
            holding.Security.Currency);
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
        if (dailyPrices.Count == 0) missingData.Add("daily");
        if (news.Count == 0) missingData.Add("news");

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
            null,
            item.Security.Currency);
    }

    private async Task<MarketPriceResult> FetchLatestPriceAsync(Security security, CancellationToken cancellationToken)
    {
        var today = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);
        var tomorrow = today.AddDays(1);
        var reusableSnapshots = await db.MarketPriceSnapshots
            .Where(snapshot =>
                snapshot.SecurityId == security.Id &&
                snapshot.Price != null &&
                !snapshot.UsedFallback)
            .ToListAsync(cancellationToken);
        var cached = reusableSnapshots
            .Where(snapshot => snapshot.FetchedAt >= today && snapshot.FetchedAt < tomorrow)
            .OrderByDescending(snapshot => snapshot.FetchedAt)
            .FirstOrDefault();

        if (cached is not null)
        {
            return new MarketPriceResult(cached.Price, cached.Currency, false, cached.IsStale, null);
        }

        var result = await marketData.GetLatestPriceAsync(security, cancellationToken);
        if (result.Price is null)
        {
            return result;
        }

        reusableSnapshots = await db.MarketPriceSnapshots
            .Where(snapshot =>
                snapshot.SecurityId == security.Id &&
                snapshot.Price != null &&
                !snapshot.UsedFallback)
            .ToListAsync(cancellationToken);
        if (reusableSnapshots.Any(snapshot => snapshot.FetchedAt >= today && snapshot.FetchedAt < tomorrow))
        {
            return result;
        }

        db.MarketPriceSnapshots.Add(new MarketPriceSnapshot
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

    private async Task<(AnalysisInput Input, ScoreResult Score, DecisionResult Decision, AiAnalysisResultDto Ai)> AnalyzeAsync(AnalysisInput input, CancellationToken cancellationToken)
    {
        var score = scoreCalculator.Calculate(input);
        var decision = decisionResolver.Resolve(input, score);
        var newsSummaries = (input.News ?? Array.Empty<NewsSentimentData>())
            .Select(news => string.IsNullOrWhiteSpace(news.Summary) ? news.Title : $"{news.Title}: {news.Summary}")
            .ToList();
        var aiRequest = new AiAnalysisRequestDto(input.Symbol, input.Name, input.TargetType, score, decision, newsSummaries, score.MissingData);
        var aiResult = await ai.AnalyzeAsync(aiRequest, cancellationToken);
        var conflict = aiResult.Succeeded && aiResult.Decision is not null && aiResult.Decision.Value != decision.Decision;

        var analysis = new AnalysisResult
        {
            SecurityId = input.SecurityId,
            AnalysisDate = DateOnly.FromDateTime(DateTime.UtcNow),
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
            Reason = BuildReason(decision, score, aiResult),
            MissingData = string.Join(",", score.MissingData),
            DecisionConflict = conflict,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.AnalysisResults.Add(analysis);
        db.AiAnalysisLogs.Add(new AiAnalysisLog
        {
            AnalysisResult = analysis,
            RequestJson = JsonSerializer.Serialize(aiRequest),
            ResponseJson = aiResult.RawJson,
            Model = "configured",
            Succeeded = aiResult.Succeeded,
            ErrorMessage = aiResult.ErrorMessage,
            CreatedAt = DateTimeOffset.UtcNow
        });

        return (input, score, decision, aiResult);
    }

    private static void AppendAnalysis(StringBuilder lines, (AnalysisInput Input, ScoreResult Score, DecisionResult Decision, AiAnalysisResultDto Ai) entry)
    {
        var rate = entry.Score.UnrealizedProfitLossRate is null ? "N/A" : $"{entry.Score.UnrealizedProfitLossRate:+0.##;-0.##;0}%";
        lines.AppendLine($"- {entry.Input.Symbol} {entry.Input.Name}: {entry.Decision.Decision} / スコア {entry.Score.TotalScore:0.##} / 含み損益率 {rate}");
        lines.AppendLine($"  理由: {entry.Decision.Reason}");
        if (!entry.Ai.Succeeded)
        {
            lines.AppendLine("  AI分析: 未実行または失敗");
        }

        foreach (var warning in (entry.Score.Warnings ?? Array.Empty<string>()).Take(3))
        {
            lines.AppendLine($"  警告: {warning}");
        }
    }

    private static string BuildReason(DecisionResult decision, ScoreResult score, AiAnalysisResultDto aiResult)
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

        if (aiResult.Succeeded && !string.IsNullOrWhiteSpace(aiResult.Reason))
        {
            details.Add($"AI補足: {aiResult.Reason}");
        }

        return string.Join(" ", details);
    }

    private static bool IsImportant(BotDecision decision) =>
        decision is BotDecision.TakeProfit or BotDecision.PartialTakeProfit or BotDecision.StopLoss or BotDecision.PartialStopLoss or BotDecision.NewBuy;
}
