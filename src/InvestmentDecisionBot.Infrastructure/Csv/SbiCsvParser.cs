using System.Globalization;
using System.Text;
using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Application.DTOs;

namespace InvestmentDecisionBot.Infrastructure.Csv;

public sealed class SbiCsvParser : ISbiCsvParser
{
    private static readonly string[] RequiredColumns =
    [
        "銘柄コード",
        "銘柄名称",
        "保有株数",
        "取得単価"
    ];

    public async Task<SbiCsvParseResult> ParseAsync(Stream csvStream, string? fileName, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        await csvStream.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();
        var (text, encodingName) = Decode(bytes);
        var rows = ParseRows(text);
        var headerIndex = FindStockHeaderIndex(rows);
        if (headerIndex < 0)
        {
            throw new InvalidOperationException("株式セクションが見つかりません。");
        }

        var header = rows[headerIndex].Select(NormalizeColumn).ToList();
        foreach (var required in RequiredColumns)
        {
            if (!header.Contains(required))
            {
                throw new InvalidOperationException($"必須カラムがありません: {required}");
            }
        }

        var holdings = new List<ParsedSbiHolding>();
        var skippedSummary = 0;
        for (var i = headerIndex + 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count == 0 || row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (LooksLikeSectionHeader(row) && i != headerIndex + 1)
            {
                break;
            }

            var symbol = Get(row, header, "銘柄コード");
            if (string.IsNullOrWhiteSpace(symbol) || IsSummaryRow(symbol, row))
            {
                skippedSummary++;
                continue;
            }

            holdings.Add(new ParsedSbiHolding(
                symbol.Trim(),
                Get(row, header, "銘柄名称").Trim(),
                ParseDecimal(Get(row, header, "保有株数"), "保有株数"),
                ParseNullableDecimal(Get(row, header, "売却注文中")),
                ParseDecimal(Get(row, header, "取得単価"), "取得単価"),
                ParseNullableDecimal(Get(row, header, "現在値")),
                ParseDecimal(Get(row, header, "取得金額"), "取得金額"),
                ParseNullableDecimal(Get(row, header, "評価額")),
                ParseNullableDecimal(Get(row, header, "評価損益"))));
        }

        return new SbiCsvParseResult(holdings, 0, skippedSummary, encodingName);
    }

    private static (string Text, string EncodingName) Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return (Encoding.UTF8.GetString(bytes), "UTF-8 BOM");
        }

        var utf8 = new UTF8Encoding(false, true);
        try
        {
            return (utf8.GetString(bytes), "UTF-8");
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var cp932 = Encoding.GetEncoding(932);
            return (cp932.GetString(bytes), "CP932");
        }
    }

    private static List<List<string>> ParseRows(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                row.Add(cell.ToString());
                cell.Clear();
                continue;
            }

            if ((ch == '\r' || ch == '\n') && !inQuotes)
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
                row.Add(cell.ToString());
                cell.Clear();
                rows.Add(row);
                row = [];
                continue;
            }

            cell.Append(ch);
        }

        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }

        return rows;
    }

    private static int FindStockHeaderIndex(List<List<string>> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var normalized = rows[i].Select(NormalizeColumn).ToHashSet();
            if (RequiredColumns.All(normalized.Contains))
            {
                return i;
            }
        }

        return -1;
    }

    private static string Get(IReadOnlyList<string> row, IReadOnlyList<string> header, string column)
    {
        var index = -1;
        for (var i = 0; i < header.Count; i++)
        {
            if (header[i] == column)
            {
                index = i;
                break;
            }
        }

        return index >= 0 && index < row.Count ? row[index] : "";
    }

    private static string NormalizeColumn(string value) => value.Trim().Replace("　", "");

    private static bool LooksLikeSectionHeader(IReadOnlyList<string> row)
    {
        var joined = string.Join(",", row).Trim();
        return joined.Contains("投資信託", StringComparison.Ordinal)
            || joined.Contains("NISA", StringComparison.OrdinalIgnoreCase)
            || joined.Contains("銘柄コード", StringComparison.Ordinal) && joined.Contains("基準価額", StringComparison.Ordinal);
    }

    private static bool IsSummaryRow(string symbol, IReadOnlyList<string> row)
    {
        var joined = string.Join("", row);
        return symbol.Contains("合計", StringComparison.Ordinal) || joined.Contains("合計", StringComparison.Ordinal) || joined.Contains("資産", StringComparison.Ordinal);
    }

    private static decimal ParseDecimal(string value, string column)
    {
        var parsed = ParseNullableDecimal(value);
        return parsed ?? throw new InvalidOperationException($"{column} の数値変換に失敗しました。");
    }

    private static decimal? ParseNullableDecimal(string value)
    {
        value = value.Trim().Replace(",", "").Replace("+", "");
        if (string.IsNullOrWhiteSpace(value) || value is "-" or "--")
        {
            return null;
        }

        if (decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"数値変換に失敗しました: {value}");
    }
}
