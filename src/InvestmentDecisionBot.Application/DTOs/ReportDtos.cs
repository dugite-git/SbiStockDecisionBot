namespace InvestmentDecisionBot.Application.DTOs;

public sealed record ReportResult(bool Succeeded, string Content, int AnalysisCount, string? DiscordMessageId, string? ErrorMessage);

public sealed record DiscordPostResult(bool Succeeded, string? MessageId, string? ErrorMessage);
