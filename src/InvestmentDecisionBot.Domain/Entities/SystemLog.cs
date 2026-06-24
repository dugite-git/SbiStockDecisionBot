namespace InvestmentDecisionBot.Domain.Entities;

public sealed class SystemLog
{
    public int Id { get; set; }
    public string Level { get; set; } = "Information";
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Exception { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
