using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Domain.Entities;

namespace InvestmentDecisionBot.Infrastructure.Services;

public sealed class SystemLogService(IBotDbContext db) : ISystemLogService
{
    public async Task LogAsync(string level, string category, string message, Exception? exception, CancellationToken cancellationToken)
    {
        db.SystemLogs.Add(new SystemLog
        {
            Level = level,
            Category = category,
            Message = message,
            Exception = exception?.ToString(),
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
