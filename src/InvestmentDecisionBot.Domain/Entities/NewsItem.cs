namespace InvestmentDecisionBot.Domain.Entities;

public sealed class NewsItem
{
    public int Id { get; set; }
    public int? SecurityId { get; set; }
    public Security? Security { get; set; }
    public string Title { get; set; } = "";
    public string? Url { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string Source { get; set; } = "";
    public string? Summary { get; set; }
    public string? Sentiment { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
