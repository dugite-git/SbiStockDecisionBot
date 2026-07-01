using System.Text;
using Microsoft.EntityFrameworkCore;

namespace InvestmentDecisionBot.Tests.Importing;

public sealed class ImportServiceTests
{
    [Fact]
    public async Task ImportsHoldingsAndDetectsSoldSymbols()
    {
        using var db = new TestDb();
        var service = TestServices.CreateImportService(db.Context);

        var first = await service.ImportSbiCsvAsync(StreamFrom("""
銘柄コード,銘柄名称,保有株数,売却注文中,取得単価,現在値,取得金額,評価額,評価損益
7203,トヨタ,100,0,2000,2500,200000,250000,+50000
9432,NTT,10,0,150,145,1500,1450,-50
NVDA,NVIDIA,1,0,100,120,100,120,+20
"""), "first.csv", CancellationToken.None);
        var second = await service.ImportSbiCsvAsync(StreamFrom("""
銘柄コード,銘柄名称,保有株数,売却注文中,取得単価,現在値,取得金額,評価額,評価損益
7203,トヨタ,120,0,2100,2600,252000,312000,+60000
"""), "second.csv", CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Contains("1件スキップ", first.Message);
        Assert.Equal(1, second.SoldDetectedCount);
        Assert.Equal(1, second.WatchlistAddedCount);
        Assert.Equal(3, await db.Context.HoldingSnapshots.CountAsync());
        Assert.Single(await db.Context.SoldEvents.ToListAsync());
        Assert.Single(await db.Context.WatchlistItems.ToListAsync());

        var batches = await db.Context.ImportBatches.OrderBy(batch => batch.Id).ToListAsync();
        Assert.Equal(2, batches.Count);
        Assert.Equal("first.csv", batches[0].SourceCsvFileName);
        Assert.Equal(first.EncodingName, batches[0].EncodingName);
        Assert.Equal(2, batches[0].ImportedCount);
        Assert.Equal(2, batches[0].CreatedCount);
        Assert.Equal(0, batches[0].UpdatedCount);
        Assert.Equal(0, batches[0].SoldCount);
        Assert.Equal(0, batches[0].WatchAddedCount);
        Assert.Equal(1, batches[0].SkippedCount);
        Assert.True(batches[0].Succeeded);
        Assert.Equal("second.csv", batches[1].SourceCsvFileName);
        Assert.Equal(second.EncodingName, batches[1].EncodingName);
        Assert.Equal(1, batches[1].ImportedCount);
        Assert.Equal(0, batches[1].CreatedCount);
        Assert.Equal(1, batches[1].UpdatedCount);
        Assert.Equal(1, batches[1].SoldCount);
        Assert.Equal(1, batches[1].WatchAddedCount);
        Assert.Equal(0, batches[1].SkippedCount);
        Assert.True(batches[1].Succeeded);
        Assert.All(await db.Context.HoldingSnapshots.ToListAsync(), snapshot => Assert.NotNull(snapshot.ImportBatchId));
        Assert.NotNull((await db.Context.SoldEvents.SingleAsync()).ImportBatchId);
    }

    [Fact]
    public async Task DoesNotUpdateDatabaseWhenCsvIsInvalid()
    {
        using var db = new TestDb();
        var service = TestServices.CreateImportService(db.Context);

        var result = await service.ImportSbiCsvAsync(StreamFrom("bad,csv\n1,2"), "bad.csv", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(await db.Context.Holdings.ToListAsync());
        Assert.Empty(await db.Context.ImportBatches.ToListAsync());
    }

    private static Stream StreamFrom(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));
}
