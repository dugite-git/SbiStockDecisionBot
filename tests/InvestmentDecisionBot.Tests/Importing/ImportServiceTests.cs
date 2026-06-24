using System.Text;
using InvestmentDecisionBot.Application.Importing;
using InvestmentDecisionBot.Infrastructure.Csv;
using Microsoft.EntityFrameworkCore;

namespace InvestmentDecisionBot.Tests.Importing;

public sealed class ImportServiceTests
{
    [Fact]
    public async Task ImportsHoldingsAndDetectsSoldSymbols()
    {
        using var db = new TestDb();
        var service = new ImportService(new SbiCsvParser(), db.Context, new NoopSystemLogService());

        var first = await service.ImportSbiCsvAsync(StreamFrom("""
銘柄コード,銘柄名称,保有株数,売却注文中,取得単価,現在値,取得金額,評価額,評価損益
7203,トヨタ,100,0,2000,2500,200000,250000,+50000
9432,NTT,10,0,150,145,1500,1450,-50
"""), "first.csv", CancellationToken.None);
        var second = await service.ImportSbiCsvAsync(StreamFrom("""
銘柄コード,銘柄名称,保有株数,売却注文中,取得単価,現在値,取得金額,評価額,評価損益
7203,トヨタ,120,0,2100,2600,252000,312000,+60000
"""), "second.csv", CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(1, second.SoldDetectedCount);
        Assert.Equal(1, second.WatchlistAddedCount);
        Assert.Equal(3, await db.Context.HoldingSnapshots.CountAsync());
        Assert.Single(await db.Context.SoldEvents.ToListAsync());
        Assert.Single(await db.Context.WatchlistItems.ToListAsync());
    }

    [Fact]
    public async Task DoesNotUpdateDatabaseWhenCsvIsInvalid()
    {
        using var db = new TestDb();
        var service = new ImportService(new SbiCsvParser(), db.Context, new NoopSystemLogService());

        var result = await service.ImportSbiCsvAsync(StreamFrom("bad,csv\n1,2"), "bad.csv", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(await db.Context.Holdings.ToListAsync());
    }

    private static Stream StreamFrom(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));
}
