using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Application.DTOs;
using InvestmentDecisionBot.Domain.Enums;
using Microsoft.Extensions.Options;

namespace InvestmentDecisionBot.Infrastructure.Providers;

public sealed class OpenAiAnalysisClient(HttpClient httpClient, IOptions<OpenAiOptions> options) : IAiAnalysisClient
{
    public async Task<AiAnalysisResultDto> AnalyzeAsync(AiAnalysisRequestDto request, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled || string.IsNullOrWhiteSpace(options.Value.ApiKey))
        {
            return new AiAnalysisResultDto(false, null, null, 0m, "", [], [], null, "AI analysis is disabled or not configured.");
        }

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ApiKey);
            httpRequest.Content = JsonContent.Create(new
            {
                model = options.Value.Model,
                response_format = new { type = "json_object" },
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = "You summarize investment-analysis inputs in Japanese as reference information only. Do not give definitive buy/sell instructions. Return JSON only."
                    },
                    new
                    {
                        role = "user",
                        content = JsonSerializer.Serialize(new
                        {
                            request.Symbol,
                            request.Name,
                            request.TargetType,
                            request.Score,
                            BotDecision = request.BotDecision,
                            request.NewsSummaries,
                            request.MissingData,
                            requiredJson = new
                            {
                                decision = request.BotDecision.Decision.ToString(),
                                sellReasonType = request.BotDecision.SellReasonType.ToString(),
                                confidence = request.BotDecision.Confidence,
                                reason = "string",
                                risks = new[] { "string" },
                                watchPoints = new[] { "string" }
                            }
                        })
                    }
                }
            });

            using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new AiAnalysisResultDto(false, null, null, 0m, "", [], [], raw, $"OpenAI request failed: {(int)response.StatusCode}");
            }

            using var document = JsonDocument.Parse(raw);
            var content = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(content))
            {
                return new AiAnalysisResultDto(false, null, null, 0m, "", [], [], raw, "OpenAI response content was empty.");
            }

            using var contentJson = JsonDocument.Parse(content);
            var root = contentJson.RootElement;
            _ = Enum.TryParse<BotDecision>(GetString(root, "decision"), out var aiDecision);
            _ = Enum.TryParse<SellReasonType>(GetString(root, "sellReasonType"), out var sellReason);
            var confidence = root.TryGetProperty("confidence", out var confidenceElement) && confidenceElement.TryGetDecimal(out var parsedConfidence)
                ? parsedConfidence
                : 0m;

            return new AiAnalysisResultDto(
                true,
                aiDecision,
                sellReason,
                confidence,
                GetString(root, "reason") ?? "",
                GetStringArray(root, "risks"),
                GetStringArray(root, "watchPoints"),
                content,
                null);
        }
        catch (Exception ex)
        {
            return new AiAnalysisResultDto(false, null, null, 0m, "", [], [], null, ex.Message);
        }
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static IReadOnlyList<string> GetStringArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return array.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .ToList();
    }
}
