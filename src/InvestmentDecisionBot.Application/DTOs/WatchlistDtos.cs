using InvestmentDecisionBot.Domain.Enums;

namespace InvestmentDecisionBot.Application.DTOs;

public sealed record WatchlistItemDto(string Symbol, string Name, WatchlistSource Source, bool IsHolding);

public sealed record WatchTargetDto(
    string Symbol,
    string Name,
    string SecurityType,
    string? Market,
    string? Country,
    string? Currency,
    string? ExternalSymbol,
    string? ExternalSymbolResolutionError,
    bool IsHolding,
    bool IsWatchlisted,
    WatchlistSource? WatchlistSource);

public sealed record WatchlistMutationResult(bool Succeeded, string Message);
