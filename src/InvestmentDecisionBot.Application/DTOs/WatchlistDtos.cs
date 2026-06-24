using InvestmentDecisionBot.Domain.Enums;

namespace InvestmentDecisionBot.Application.DTOs;

public sealed record WatchlistItemDto(string Symbol, string Name, WatchlistSource Source, bool IsHolding);

public sealed record WatchlistMutationResult(bool Succeeded, string Message);
