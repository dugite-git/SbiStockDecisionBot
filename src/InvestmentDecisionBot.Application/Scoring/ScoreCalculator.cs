using InvestmentDecisionBot.Application.Abstractions;
using InvestmentDecisionBot.Application.DTOs;
using InvestmentDecisionBot.Domain.Enums;

namespace InvestmentDecisionBot.Application.Scoring;

public sealed class ScoreCalculator : IScoreCalculator
{
    public ScoreResult Calculate(AnalysisInput input)
    {
        var missingData = input.MissingData.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var reasons = new List<string>();
        var warnings = new List<string>();

        AddMissing(missingData, "fundamental");
        AddMissing(missingData, "quality");
        var fundamental = Neutral("外部財務データのキャッシュがないため、FundamentalScoreは中立値で評価しています。");
        var quality = Neutral("外部品質・収益性データのキャッシュがないため、QualityScoreは中立値で評価しています。");
        if (input.FinancialSnapshot is not null)
        {
            missingData.RemoveAll(key => key.Equals("fundamental", StringComparison.OrdinalIgnoreCase) || key.Equals("quality", StringComparison.OrdinalIgnoreCase) || key.Equals("financial", StringComparison.OrdinalIgnoreCase));
            fundamental = CalculateFundamental(input.FinancialSnapshot, missingData);
            quality = CalculateQuality(input.FinancialSnapshot, missingData);
        }

        var momentum = CalculateMomentum(input, missingData);
        var news = CalculateNews(input, missingData);
        var positionRisk = CalculatePositionRisk(input, missingData);

        Add(fundamental, reasons, warnings);
        Add(quality, reasons, warnings);
        Add(momentum, reasons, warnings);
        Add(news, reasons, warnings);
        Add(positionRisk, reasons, warnings);
        if (input.CacheWarnings is not null)
        {
            warnings.AddRange(input.CacheWarnings);
        }

        var total =
            fundamental.AdjustedScore * 0.40m +
            quality.AdjustedScore * 0.20m +
            momentum.AdjustedScore * 0.20m +
            news.AdjustedScore * 0.10m +
            positionRisk.AdjustedScore * 0.10m;

        var rate = CalculateProfitLossRate(input);
        return new ScoreResult(
            fundamental.AdjustedScore,
            quality.AdjustedScore,
            momentum.AdjustedScore,
            news.AdjustedScore,
            positionRisk.AdjustedScore,
            decimal.Round(total, 2),
            rate,
            missingData,
            fundamental,
            quality,
            momentum,
            news,
            positionRisk,
            reasons,
            warnings);
    }

    private static ScoreBreakdown CalculateFundamental(FinancialSnapshotData financial, List<string> missingData)
    {
        var scoreParts = new List<decimal>();
        if (financial.OperatingProfit is not null && financial.NetSales is > 0m)
        {
            var margin = financial.OperatingProfit.Value / financial.NetSales.Value;
            scoreParts.Add(margin switch
            {
                >= 0.15m => 90m,
                >= 0.10m => 75m,
                >= 0.05m => 60m,
                >= 0m => 45m,
                _ => 20m
            });
        }

        if (financial.Profit is not null)
        {
            scoreParts.Add(financial.Profit.Value switch
            {
                > 0m => 65m,
                < 0m => 25m,
                _ => 50m
            });
        }

        if (financial.Eps is not null)
        {
            scoreParts.Add(financial.Eps.Value switch
            {
                > 0m => 65m,
                < 0m => 25m,
                _ => 50m
            });
        }

        if (scoreParts.Count == 0)
        {
            AddMissing(missingData, "fundamental");
            return Neutral("Financial cache exists but usable fundamental fields are missing.");
        }

        var raw = scoreParts.Average();
        var confidence = Math.Clamp(0.35m + scoreParts.Count * 0.15m, 0.35m, 0.75m);
        return Breakdown(raw, confidence, [$"Financial data available ({scoreParts.Count} fundamental fields)."], []);
    }

    private static ScoreBreakdown CalculateQuality(FinancialSnapshotData financial, List<string> missingData)
    {
        var scoreParts = new List<decimal>();
        if (financial.EquityRatio is not null)
        {
            var ratio = financial.EquityRatio.Value > 1m ? financial.EquityRatio.Value / 100m : financial.EquityRatio.Value;
            scoreParts.Add(ratio switch
            {
                >= 0.55m => 90m,
                >= 0.40m => 75m,
                >= 0.25m => 55m,
                >= 0.10m => 35m,
                _ => 20m
            });
        }

        if (financial.NetAssets is not null && financial.TotalAssets is > 0m)
        {
            var equityRatio = financial.NetAssets.Value / financial.TotalAssets.Value;
            scoreParts.Add(equityRatio switch
            {
                >= 0.55m => 90m,
                >= 0.40m => 75m,
                >= 0.25m => 55m,
                >= 0.10m => 35m,
                _ => 20m
            });
        }

        if (financial.OperatingProfit is not null && financial.Profit is not null)
        {
            scoreParts.Add(financial.OperatingProfit.Value > 0m && financial.Profit.Value > 0m ? 75m : financial.Profit.Value < 0m ? 25m : 50m);
        }

        if (scoreParts.Count == 0)
        {
            AddMissing(missingData, "quality");
            return Neutral("Financial cache exists but usable quality fields are missing.");
        }

        var raw = scoreParts.Average();
        var confidence = Math.Clamp(0.35m + scoreParts.Count * 0.15m, 0.35m, 0.75m);
        return Breakdown(raw, confidence, [$"Financial data available ({scoreParts.Count} quality fields)."], []);
    }

    private static ScoreBreakdown CalculateMomentum(AnalysisInput input, List<string> missingData)
    {
        var bars = (input.DailyPrices ?? Array.Empty<DailyPriceBar>()).OrderBy(bar => bar.Date).ToList();
        if (bars.Count < 30)
        {
            AddMissing(missingData, "momentum");
            return Breakdown(50m, 0.15m, [], [$"外部市場データの日足キャッシュが不足しています（{bars.Count}/30本）。MomentumScoreは中立値で評価しています。`/marketdata prefetch` で日足データを取得してください。"]);
        }

        var latest = bars[^1];
        var returnTrend = WeightedAverage(
            (ReturnScore(ReturnOverDays(bars, 21)), 0.30m),
            (ReturnScore(ReturnOverDays(bars, 63)), 0.40m),
            (ReturnScore(ReturnOverDays(bars, 126)), 0.30m));
        var movingAverage = MovingAverageScore(bars);
        var technical = TechnicalScore(bars);
        var volume = VolumeScore(bars);
        var raw = returnTrend * 0.40m + movingAverage * 0.25m + technical * 0.25m + volume * 0.10m;
        var confidence = bars.Count >= 200 ? 0.95m : bars.Count >= 75 ? 0.75m : 0.50m;
        var reasons = new List<string> { $"直近終値 {latest.Close:0.##}、モメンタム素点 {raw:0.#}。" };
        return Breakdown(raw, confidence, reasons, []);
    }

    private static ScoreBreakdown CalculateNews(AnalysisInput input, List<string> missingData)
    {
        var news = (input.News ?? Array.Empty<NewsSentimentData>()).ToList();
        if (news.Count == 0)
        {
            var fetchedButEmpty = input.CacheWarnings?.Any(warning => warning.Contains("該当記事が0件", StringComparison.OrdinalIgnoreCase)) == true;
            if (!fetchedButEmpty)
            {
                AddMissing(missingData, "news");
            }

            return Breakdown(50m, 0.10m, [], fetchedButEmpty ? [] : ["外部ニュースキャッシュがないため、ニュースは中立値で評価しています。"]);
        }

        var weighted = news.Select(item =>
        {
            var decay = item.PublishedAt is null ? 0.4m : TimeDecay(DateTimeOffset.UtcNow - item.PublishedAt.Value);
            return (Sentiment: item.SentimentScore * item.RelevanceScore * decay, Weight: Math.Max(0.05m, item.RelevanceScore * decay));
        }).ToList();
        var averageSentiment = weighted.Sum(item => item.Sentiment) / weighted.Sum(item => item.Weight);
        var sentimentScore = averageSentiment switch
        {
            >= 0.35m => 90m,
            >= 0.15m => 75m,
            >= -0.15m => 50m,
            >= -0.35m => 30m,
            _ => 15m
        };
        var relevanceScore = Math.Clamp(news.Average(item => item.RelevanceScore) * 100m, 0m, 100m);
        var eventRiskScore = averageSentiment <= -0.35m ? 20m : averageSentiment < -0.15m ? 40m : averageSentiment > 0.20m ? 85m : 70m;
        var raw = sentimentScore * 0.60m + relevanceScore * 0.25m + eventRiskScore * 0.15m;
        var confidence = Math.Clamp(0.35m + news.Count * 0.05m, 0.35m, 0.85m);
        return Breakdown(raw, confidence, [$"ニュースキャッシュ {news.Count} 件、加重センチメント {averageSentiment:0.###}。"], []);
    }

    private static ScoreBreakdown CalculatePositionRisk(AnalysisInput input, List<string> missingData)
    {
        if (input.TargetType != TargetType.Holding)
        {
            return Breakdown(70m * 0.25m + 80m * 0.25m + 50m * 0.25m + 50m * 0.15m + CurrencyRiskScore(input) * 0.10m, 0.45m, [], []);
        }

        var rate = CalculateProfitLossRate(input);
        var pnlScore = rate is null ? 50m : UnrealizedPnLScore(rate.Value);
        var concentration = ConcentrationScore(input);
        var volatility = VolatilityScore(input.DailyPrices, missingData);
        var liquidity = LiquidityScore(input.DailyPrices, missingData);
        var currency = CurrencyRiskScore(input);
        var raw = pnlScore * 0.25m + concentration * 0.25m + volatility * 0.25m + liquidity * 0.15m + currency * 0.10m;
        var confidence = 0.55m;
        if (rate is not null) confidence += 0.15m;
        if ((input.DailyPrices?.Count ?? 0) >= 60) confidence += 0.20m;
        return Breakdown(raw, Math.Clamp(confidence, 0.30m, 0.95m), [$"ポジションリスク素点 {raw:0.#}。"], rate is null ? ["含み損益率を計算できないため、一部を中立値で評価しています。"] : []);
    }

    private static decimal MovingAverageScore(IReadOnlyList<DailyPriceBar> bars)
    {
        var latest = bars[^1].Close;
        var sma25 = Sma(bars, 25);
        var sma75 = Sma(bars, 75);
        var sma200 = Sma(bars, 200);
        var score = 0m;
        if (sma25 is not null && latest > sma25) score += 25m;
        if (sma75 is not null && latest > sma75) score += 25m;
        if (sma25 is not null && sma75 is not null && sma25 > sma75) score += 25m;
        if (sma75 is not null && sma200 is not null && sma75 > sma200) score += 25m;
        return score == 0m && bars.Count < 200 ? 50m : score;
    }

    private static decimal TechnicalScore(IReadOnlyList<DailyPriceBar> bars)
    {
        var rsi = Rsi(bars, 14);
        var rsiScore = rsi is null ? 50m : rsi.Value switch
        {
            >= 45m and <= 65m => 80m,
            >= 35m and < 45m => 60m,
            > 65m and <= 75m => 60m,
            > 75m => 35m,
            < 30m => 30m,
            _ => 45m
        };
        var macdScore = MacdScore(bars);
        var adxScore = 50m;
        return rsiScore * 0.35m + macdScore * 0.35m + adxScore * 0.30m;
    }

    private static decimal VolumeScore(IReadOnlyList<DailyPriceBar> bars)
    {
        if (bars.Count < 20) return 50m;
        var latest = bars[^1].Volume;
        var average = bars.TakeLast(20).Average(bar => (decimal)bar.Volume);
        if (average <= 0) return 50m;
        if (latest > average * 1.5m) return 80m;
        if (latest > average) return 65m;
        if (latest < average * 0.5m) return 35m;
        return 50m;
    }

    private static decimal VolatilityScore(IReadOnlyList<DailyPriceBar>? dailyPrices, List<string> missingData)
    {
        var bars = (dailyPrices ?? Array.Empty<DailyPriceBar>()).OrderBy(bar => bar.Date).ToList();
        if (bars.Count < 61)
        {
            AddMissing(missingData, "volatility");
            return 50m;
        }

        var returns = bars.TakeLast(61).Zip(bars.TakeLast(61).Skip(1), (previous, current) => previous.Close <= 0 ? 0m : current.Close / previous.Close - 1m).ToList();
        var average = returns.Average();
        var variance = returns.Sum(value => (value - average) * (value - average)) / returns.Count;
        var volatility = (decimal)Math.Sqrt((double)variance);
        return volatility switch
        {
            <= 0.01m => 90m,
            <= 0.02m => 75m,
            <= 0.035m => 60m,
            <= 0.055m => 35m,
            _ => 15m
        };
    }

    private static decimal LiquidityScore(IReadOnlyList<DailyPriceBar>? dailyPrices, List<string> missingData)
    {
        var bars = (dailyPrices ?? Array.Empty<DailyPriceBar>()).OrderBy(bar => bar.Date).ToList();
        if (bars.Count < 20)
        {
            AddMissing(missingData, "liquidity");
            return 50m;
        }

        var value = bars.TakeLast(20).Average(bar => bar.Close * bar.Volume);
        return value switch
        {
            >= 10_000_000_000m => 90m,
            >= 1_000_000_000m => 75m,
            >= 100_000_000m => 60m,
            >= 20_000_000m => 40m,
            _ => 25m
        };
    }

    private static decimal CurrencyRiskScore(AnalysisInput input)
    {
        return 70m;
    }

    private static decimal ConcentrationScore(AnalysisInput input)
    {
        if (input.MarketValue is not > 0m || input.TotalPortfolioMarketValue is not > 0m) return 50m;
        var ratio = input.MarketValue.Value / input.TotalPortfolioMarketValue.Value;
        return ratio switch
        {
            < 0.05m => 90m,
            < 0.10m => 75m,
            < 0.20m => 50m,
            < 0.30m => 25m,
            _ => 10m
        };
    }

    private static decimal UnrealizedPnLScore(decimal rate) => rate switch
    {
        >= 20m => 75m,
        >= 5m => 85m,
        >= -5m => 70m,
        >= -10m => 45m,
        >= -20m => 25m,
        _ => 10m
    };

    private static decimal ReturnScore(decimal? value) => value switch
    {
        null => 50m,
        >= 0.20m => 90m,
        >= 0.10m => 75m,
        >= 0m => 60m,
        >= -0.10m => 40m,
        _ => 20m
    };

    private static decimal? ReturnOverDays(IReadOnlyList<DailyPriceBar> bars, int days)
    {
        var pastIndex = bars.Count - 1 - days;
        if (pastIndex < 0 || bars[^1].Close <= 0m || bars[pastIndex].Close <= 0m) return null;
        return bars[^1].Close / bars[pastIndex].Close - 1m;
    }

    private static decimal? Sma(IReadOnlyList<DailyPriceBar> bars, int days) =>
        bars.Count < days ? null : bars.TakeLast(days).Average(bar => bar.Close);

    private static decimal? Rsi(IReadOnlyList<DailyPriceBar> bars, int period)
    {
        if (bars.Count <= period) return null;
        var gains = 0m;
        var losses = 0m;
        foreach (var pair in bars.TakeLast(period + 1).Zip(bars.TakeLast(period + 1).Skip(1)))
        {
            var change = pair.Second.Close - pair.First.Close;
            if (change >= 0) gains += change; else losses -= change;
        }

        if (losses == 0m) return 100m;
        var rs = gains / losses;
        return 100m - 100m / (1m + rs);
    }

    private static decimal MacdScore(IReadOnlyList<DailyPriceBar> bars)
    {
        if (bars.Count < 35) return 50m;
        var ema12 = Ema(bars.Select(bar => bar.Close), 12).ToList();
        var ema26 = Ema(bars.Select(bar => bar.Close), 26).ToList();
        var offset = ema12.Count - ema26.Count;
        var macd = ema26.Select((value, index) => ema12[index + offset] - value).ToList();
        if (macd.Count < 9) return 50m;
        var signal = Ema(macd, 9).Last();
        var latest = macd[^1];
        return latest > signal ? 70m : latest > 0m ? 55m : 35m;
    }

    private static IEnumerable<decimal> Ema(IEnumerable<decimal> values, int period)
    {
        var list = values.ToList();
        if (list.Count < period) yield break;
        var multiplier = 2m / (period + 1m);
        var ema = list.Take(period).Average();
        yield return ema;
        foreach (var value in list.Skip(period))
        {
            ema = (value - ema) * multiplier + ema;
            yield return ema;
        }
    }

    private static decimal? CalculateProfitLossRate(AnalysisInput input)
    {
        if (input.MarketValue is > 0m && input.UnrealizedProfitLoss is not null)
        {
            var invested = input.MarketValue.Value - input.UnrealizedProfitLoss.Value;
            if (invested > 0m)
            {
                return decimal.Round(input.UnrealizedProfitLoss.Value / invested * 100m, 2);
            }
        }

        if (input.AverageAcquisitionPrice is > 0m && input.CurrentPrice is not null)
        {
            return decimal.Round((input.CurrentPrice.Value - input.AverageAcquisitionPrice.Value) / input.AverageAcquisitionPrice.Value * 100m, 2);
        }

        return null;
    }

    private static ScoreBreakdown Neutral(string warning) =>
        Breakdown(50m, 0.10m, [], [warning]);

    private static ScoreBreakdown Breakdown(decimal raw, decimal confidence, IReadOnlyList<string> reasons, IReadOnlyList<string> warnings)
    {
        var adjusted = raw * confidence + 50m * (1m - confidence);
        return new ScoreBreakdown(decimal.Round(raw, 2), confidence, decimal.Round(adjusted, 2), reasons, warnings);
    }

    private static decimal WeightedAverage(params (decimal Score, decimal Weight)[] items) =>
        items.Sum(item => item.Score * item.Weight) / items.Sum(item => item.Weight);

    private static decimal TimeDecay(TimeSpan age)
    {
        if (age.TotalDays <= 3) return 1.0m;
        if (age.TotalDays <= 7) return 0.7m;
        if (age.TotalDays <= 14) return 0.4m;
        return 0.2m;
    }

    private static void AddMissing(List<string> missingData, string key)
    {
        if (!missingData.Contains(key, StringComparer.OrdinalIgnoreCase))
        {
            missingData.Add(key);
        }
    }

    private static void Add(ScoreBreakdown breakdown, List<string> reasons, List<string> warnings)
    {
        reasons.AddRange(breakdown.Reasons);
        warnings.AddRange(breakdown.Warnings);
    }
}
