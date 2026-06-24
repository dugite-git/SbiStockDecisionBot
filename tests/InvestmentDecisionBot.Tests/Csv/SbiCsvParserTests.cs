using System.Text;
using InvestmentDecisionBot.Infrastructure.Csv;

namespace InvestmentDecisionBot.Tests.Csv;

public sealed class SbiCsvParserTests
{
    [Fact]
    public async Task ParsesCp932StockSectionAndSkipsTrustSection()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var csv = """
投資信託
銘柄コード,銘柄名称,保有口数,基準価額
ABC,ファンド,10,100

株式
銘柄コード,銘柄名称,保有株数,売却注文中,取得単価,現在値,取得金額,評価額,評価損益
7203,トヨタ自動車,100,+0,2000,2500,200000,250000,+50000
9432,NTT,10,,150.5,145,1505,1450,-55
合計,,,,,,201505,251450,+49945
投資信託
""";
        var bytes = Encoding.GetEncoding(932).GetBytes(csv);
        await using var stream = new MemoryStream(bytes);

        var result = await new SbiCsvParser().ParseAsync(stream, "sbi.csv", CancellationToken.None);

        Assert.Equal("CP932", result.EncodingName);
        Assert.Equal(2, result.Holdings.Count);
        Assert.Equal("7203", result.Holdings[0].Symbol);
        Assert.Equal(50000m, result.Holdings[0].ImportedUnrealizedProfitLoss);
        Assert.Equal(-55m, result.Holdings[1].ImportedUnrealizedProfitLoss);
    }

    [Fact]
    public async Task FailsWhenRequiredColumnIsMissing()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("銘柄コード,銘柄名称\n7203,トヨタ"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new SbiCsvParser().ParseAsync(stream, null, CancellationToken.None));

        Assert.Contains("株式セクション", ex.Message);
    }
}
