using InvestmentDecisionBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InvestmentDecisionBot.Application.Abstractions;

public interface IBotDbContext
{
    DbSet<Security> Securities { get; }
    DbSet<Holding> Holdings { get; }
    DbSet<HoldingSnapshot> HoldingSnapshots { get; }
    DbSet<WatchlistItem> WatchlistItems { get; }
    DbSet<SoldEvent> SoldEvents { get; }
    DbSet<MarketPriceSnapshot> MarketPriceSnapshots { get; }
    DbSet<NewsItem> NewsItems { get; }
    DbSet<AnalysisResult> AnalysisResults { get; }
    DbSet<DailyReport> DailyReports { get; }
    DbSet<AiAnalysisLog> AiAnalysisLogs { get; }
    DbSet<SystemLog> SystemLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
