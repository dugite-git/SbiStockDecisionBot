using InvestmentDecisionBot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace InvestmentDecisionBot.Tests;

internal sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public TestDb()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<BotDbContext>().UseSqlite(_connection).Options;
        Context = new BotDbContext(options);
        Context.Database.EnsureCreated();
    }

    public BotDbContext Context { get; }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
