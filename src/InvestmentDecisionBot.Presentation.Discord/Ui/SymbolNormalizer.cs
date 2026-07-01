namespace InvestmentDecisionBot.Presentation.Discord.Ui;

public static class SymbolNormalizer
{
    public static SymbolNormalizationResult NormalizeJapaneseStockSymbol(string? input)
    {
        var value = NormalizeRaw(input);
        return value.Length == 4 && value.All(char.IsDigit)
            ? new SymbolNormalizationResult(true, value, null)
            : new SymbolNormalizationResult(false, value, "日本株4桁コードを入力してください。例: 7203 / 7203.T / ７２０３");
    }

    public static string NormalizeRaw(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "";
        }

        var value = input.Trim().ToUpperInvariant();
        if (value.EndsWith(".T", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^2];
        }

        return string.Concat(value.Select(ConvertFullWidthDigit));
    }

    private static char ConvertFullWidthDigit(char value) =>
        value is >= '０' and <= '９'
            ? (char)('0' + value - '０')
            : value;
}

public sealed record SymbolNormalizationResult(bool IsValid, string Symbol, string? ErrorMessage);
