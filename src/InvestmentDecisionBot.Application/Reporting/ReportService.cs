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

        var lines = new StringBuilder();
        lines.AppendLine("今日の投資判断レポート");
        lines.AppendLine();
        lines.AppendLine("重要判断");

        var analyses = new List<(AnalysisInput Input, ScoreResult Score, DecisionResult Decision, AiAnalysisResultDto Ai)>();
        foreach (var holding in holdings)
        {
            var input = new AnalysisInput(
                holding.SecurityId,
                holding.Security.Symbol,
                holding.Security.Name,
                TargetType.Holding,
                holding.Quantity,
                holding.AverageAcquisitionPrice,
                holding.ImportedCurrentPrice,
                holding.ImportedMarketValue,
                holding.ImportedUnrealizedProfitLoss,
                Array.Empty<string>());
            analyses.Add(await AnalyzeAsync(input, cancellationToken));
        }

        foreach (var item in watchlist.Where(w => !holdingIds.Contains(w.SecurityId)))
        {
            var input = new AnalysisInput(item.SecurityId, item.Security.Symbol, item.Security.Name, TargetType.Watchlist, null, null, null, null, null, Array.Empty<string>());
            analyses.Add(await AnalyzeAsync(input, cancellationToken));
        }

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
        lines.AppendLine("- このレポートは投資判断の参考情報です。売買を推奨・強制するものではありません。");
        lines.AppendLine("- SBIログイン、注文発行、自動売買、資産管理は行いません。");
        if (missing.Count > 0)
        {
            lines.AppendLine($"- データ不足: {string.Join(", ", missing)} は中立スコアまたはfallbackで扱いました。");
        }

        var content = lines.ToString().TrimEnd();
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
                await logs.LogAsync("Error", "Discord", postResult.ErrorMessage ?? "Discord post failed.", null, cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new ReportResult(postResult?.Succeeded ?? true, content, analyses.Count, postResult?.MessageId, postResult?.ErrorMessage);
    }

    private async Task<(AnalysisInput Input, ScoreResult Score, DecisionResult Decision, AiAnalysisResultDto Ai)> AnalyzeAsync(AnalysisInput input, CancellationToken cancellationToken)
    {
        var score = scoreCalculator.Calculate(input);
        var decision = decisionResolver.Resolve(input, score);
        var aiRequest = new AiAnalysisRequestDto(input.Symbol, input.Name, input.TargetType, score, decision, Array.Empty<string>(), score.MissingData);
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
            Reason = aiResult.Succeeded && !string.IsNullOrWhiteSpace(aiResult.Reason) ? $"{decision.Reason} AI補足: {aiResult.Reason}" : decision.Reason,
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
        lines.AppendLine($"- {entry.Input.Symbol} {entry.Input.Name}: {entry.Decision.Decision} / Score {entry.Score.TotalScore:0.##} / 損益率 {rate}");
        lines.AppendLine($"  参考: {entry.Decision.Reason}");
        if (!entry.Ai.Succeeded)
        {
            lines.AppendLine("  AI分析: 未実行または失敗");
        }
    }

    private static bool IsImportant(BotDecision decision) =>
        decision is BotDecision.TakeProfit or BotDecision.PartialTakeProfit or BotDecision.StopLoss or BotDecision.PartialStopLoss or BotDecision.NewBuy;
}
