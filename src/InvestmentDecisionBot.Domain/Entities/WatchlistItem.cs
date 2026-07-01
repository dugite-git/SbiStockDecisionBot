using InvestmentDecisionBot.Domain.Enums;

namespace InvestmentDecisionBot.Domain.Entities;

public sealed class WatchlistItem
{
    public int Id { get; set; }
    public int SecurityId { get; set; }
    public Security Security { get; set; } = null!;
    public WatchlistSource Source { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RemovedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        RemovedAt = now;
        UpdatedAt = now;
    }
}
