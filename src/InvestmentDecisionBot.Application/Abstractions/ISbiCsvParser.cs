using InvestmentDecisionBot.Application.DTOs;

namespace InvestmentDecisionBot.Application.Abstractions;

public interface ISbiCsvParser
{
    Task<SbiCsvParseResult> ParseAsync(Stream csvStream, string? fileName, CancellationToken cancellationToken);
}
