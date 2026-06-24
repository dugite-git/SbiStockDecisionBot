namespace InvestmentDecisionBot.Domain.Entities;

public sealed class Holding
{
    public int Id { get; set; }
    public int SecurityId { get; set; }
    public Security Security { get; set; } = null!;
    public decimal Quantity { get; set; }
    public decimal? PendingSellQuantity { get; set; }
    public decimal AverageAcquisitionPrice { get; set; }
    public decimal AcquisitionAmount { get; set; }
    public decimal? ImportedCurrentPrice { get; set; }
    public decimal? ImportedMarketValue { get; set; }
    public decimal? ImportedUnrealizedProfitLoss { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
