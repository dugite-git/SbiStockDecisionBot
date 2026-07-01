using InvestmentDecisionBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InvestmentDecisionBot.Infrastructure.Persistence;

public sealed class BotDbContext(DbContextOptions<BotDbContext> options) : DbContext(options)
{
    public DbSet<Security> Securities => Set<Security>();
    public DbSet<Holding> Holdings => Set<Holding>();
    public DbSet<HoldingSnapshot> HoldingSnapshots => Set<HoldingSnapshot>();
    public DbSet<WatchlistItem> WatchlistItems => Set<WatchlistItem>();
    public DbSet<SoldEvent> SoldEvents => Set<SoldEvent>();
    public DbSet<MarketPriceSnapshot> MarketPriceSnapshots => Set<MarketPriceSnapshot>();
    public DbSet<ExternalApiCacheEntry> ExternalApiCacheEntries => Set<ExternalApiCacheEntry>();
    public DbSet<ExternalApiRequestLog> ExternalApiRequestLogs => Set<ExternalApiRequestLog>();
    public DbSet<NewsItem> NewsItems => Set<NewsItem>();
    public DbSet<AnalysisResult> AnalysisResults => Set<AnalysisResult>();
    public DbSet<DailyReport> DailyReports => Set<DailyReport>();
    public DbSet<AiAnalysisLog> AiAnalysisLogs => Set<AiAnalysisLog>();
    public DbSet<SystemLog> SystemLogs => Set<SystemLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Security>(entity =>
        {
            entity.HasIndex(e => new { e.SecurityType, e.Symbol }).IsUnique();
            entity.Property(e => e.Symbol).HasMaxLength(32);
            entity.Property(e => e.Name).HasMaxLength(256);
            entity.Property(e => e.ExternalSymbol).HasMaxLength(64);
        });

        modelBuilder.Entity<Holding>(entity =>
        {
            entity.HasIndex(e => e.SecurityId).IsUnique();
            entity.Property(e => e.Quantity).HasPrecision(18, 4);
            entity.Property(e => e.PendingSellQuantity).HasPrecision(18, 4);
            entity.Property(e => e.AverageAcquisitionPrice).HasPrecision(18, 4);
            entity.Property(e => e.AcquisitionAmount).HasPrecision(18, 4);
            entity.Property(e => e.ImportedCurrentPrice).HasPrecision(18, 4);
            entity.Property(e => e.ImportedMarketValue).HasPrecision(18, 4);
            entity.Property(e => e.ImportedUnrealizedProfitLoss).HasPrecision(18, 4);
        });

        modelBuilder.Entity<HoldingSnapshot>(entity =>
        {
            entity.Property(e => e.Quantity).HasPrecision(18, 4);
            entity.Property(e => e.PendingSellQuantity).HasPrecision(18, 4);
            entity.Property(e => e.AverageAcquisitionPrice).HasPrecision(18, 4);
            entity.Property(e => e.AcquisitionAmount).HasPrecision(18, 4);
            entity.Property(e => e.ImportedCurrentPrice).HasPrecision(18, 4);
            entity.Property(e => e.ImportedMarketValue).HasPrecision(18, 4);
            entity.Property(e => e.ImportedUnrealizedProfitLoss).HasPrecision(18, 4);
        });

        modelBuilder.Entity<WatchlistItem>().HasIndex(e => new { e.SecurityId, e.IsActive });
        modelBuilder.Entity<SoldEvent>().HasIndex(e => e.SecurityId);
        modelBuilder.Entity<MarketPriceSnapshot>(entity =>
        {
            entity.Property(e => e.FetchedAt).HasConversion<string>();
        });
        modelBuilder.Entity<ExternalApiCacheEntry>(entity =>
        {
            entity.HasIndex(e => new { e.Provider, e.Function, e.CacheKey }).IsUnique();
            entity.HasIndex(e => e.ExpiresAt);
            entity.Property(e => e.Provider).HasMaxLength(64);
            entity.Property(e => e.Function).HasMaxLength(64);
            entity.Property(e => e.CacheKey).HasMaxLength(256);
        });
        modelBuilder.Entity<ExternalApiRequestLog>(entity =>
        {
            entity.HasIndex(e => new { e.Provider, e.RequestedAt });
            entity.Property(e => e.Provider).HasMaxLength(64);
            entity.Property(e => e.Function).HasMaxLength(64);
            entity.Property(e => e.CacheKey).HasMaxLength(256);
        });
        modelBuilder.Entity<AnalysisResult>().HasIndex(e => new { e.SecurityId, e.AnalysisDate, e.TargetType });
        modelBuilder.Entity<DailyReport>().HasIndex(e => e.ReportDate);
        modelBuilder.Entity<SystemLog>().HasIndex(e => e.CreatedAt);
    }
}
