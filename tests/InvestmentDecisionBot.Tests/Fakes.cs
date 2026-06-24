using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Application.DTOs;

namespace InvestmentDecisionBot.Tests;

internal sealed class NoopSystemLogService : ISystemLogService
{
    public List<string> Messages { get; } = [];

    public Task LogAsync(string level, string category, string message, Exception? exception, CancellationToken cancellationToken)
    {
        Messages.Add(message);
        return Task.CompletedTask;
    }
}

internal sealed class FakeDiscordPublisher(bool succeeds) : IDiscordReportPublisher
{
    public Task<DiscordPostResult> PostReportAsync(string content, CancellationToken cancellationToken) =>
        Task.FromResult(succeeds
            ? new DiscordPostResult(true, "123", null)
            : new DiscordPostResult(false, null, "post failed"));
}

internal sealed class FakeAiAnalysisClient(bool succeeds) : IAiAnalysisClient
{
    public Task<AiAnalysisResultDto> AnalyzeAsync(AiAnalysisRequestDto request, CancellationToken cancellationToken) =>
        Task.FromResult(succeeds
            ? new AiAnalysisResultDto(true, request.BotDecision.Decision, request.BotDecision.SellReasonType, 0.7m, "補足情報です。", [], [], "{\"ok\":true}", null)
            : new AiAnalysisResultDto(false, null, null, 0m, "", [], [], null, "disabled"));
}
