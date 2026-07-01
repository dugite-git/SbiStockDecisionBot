# スコアリング仕様

このドキュメントは、Botが日本株の保有銘柄・ウォッチリスト銘柄をどうスコアリングし、最終的な `BotDecision` を決めるかを説明します。投資助言や自動売買ではなく、判断材料を整理するためのルールベースレポートです。

## 対象

| TargetType | 対象 | 主な用途 |
| --- | --- | --- |
| `Holding` | SBI CSVから取り込んだ日本株4桁コードの保有銘柄 | 保有継続、利確候補、損切り候補、リスク確認 |
| `Watchlist` | `/watch add` で登録した日本株4桁コード | 新規買い候補、監視継続 |

既存DBに米国株などが残っていても、レポート対象には含めません。

## 入力データ

主な入力は `AnalysisInput` です。

| 項目 | 内容 |
| --- | --- |
| `SecurityId` | DB上の銘柄ID |
| `Symbol` | 4桁銘柄コード |
| `Name` | 銘柄名 |
| `TargetType` | `Holding` または `Watchlist` |
| `Quantity` | 保有数量 |
| `AverageAcquisitionPrice` | 平均取得単価 |
| `CurrentPrice` | 現在価格 |
| `MarketValue` | 評価額 |
| `UnrealizedProfitLoss` | 評価損益 |
| `DailyPrices` | 外部市場データ由来の日足OHLCV |
| `News` | 外部ニュースデータ |
| `FinancialSnapshot` | J-Quants財務キャッシュ由来の財務・品質データ |
| `TotalPortfolioMarketValue` | 日本株保有銘柄全体の評価額 |
| `Currency` | 原則 `JPY` |
| `MissingData` | 不足しているデータ種別 |

## 総合スコア

各サブスコアは0から100で扱い、高いほど良い評価です。

```text
TotalScore =
  FundamentalScore   * 0.40
+ QualityScore       * 0.20
+ MomentumScore      * 0.20
+ NewsScore          * 0.10
+ PositionRiskScore  * 0.10
```

外部データ不足時は中立値とconfidence補正で扱います。

```text
AdjustedScore = RawScore * Confidence + 50 * (1 - Confidence)
```

## サブスコア

| スコア | 現在の実装 |
| --- | --- |
| `FundamentalScore` | 財務データがあれば利益率・黒字性・EPSを評価。未取得時は中立値50を低confidenceで使用 |
| `QualityScore` | 財務データがあれば自己資本比率・資産健全性・利益品質を評価。未取得時は中立値50を低confidenceで使用 |
| `MomentumScore` | 日足からリターン、移動平均、RSI、MACD、出来高を評価 |
| `NewsScore` | ニュースのsentimentとrelevanceを評価。データなしは中立値 |
| `PositionRiskScore` | 含み損益、集中度、volatility、liquidity、通貨リスクを評価 |

各サブスコアは、まず `RawScore` を計算し、その後 `Confidence` で50点方向へ補正した `AdjustedScore` を最終的なスコアとして使います。`TotalScore` に入るのは `RawScore` ではなく `AdjustedScore` です。

```text
AdjustedScore = RawScore * Confidence + 50 * (1 - Confidence)
```

たとえば `RawScore = 80`, `Confidence = 0.50` の場合、最終スコアは `65` になります。データが少ないほど極端な評価を避け、中立値50に近づけます。

## FundamentalScore

`FinancialSnapshot` がある場合、利用可能な財務項目を点数化し、その平均を `RawScore` にします。利用可能な項目がない場合は `MissingData` に `fundamental` を追加し、中立値50を低confidenceで使います。

```text
FundamentalScore.Raw =
  Average(OperatingMarginScore, ProfitScore, EpsScore)
```

利用可能な項目だけを平均します。

| 評価項目 | 条件 | 点数 |
| --- | --- | --- |
| 営業利益率 `OperatingProfit / NetSales` | `>= 15%` | 90 |
| 同上 | `>= 10%` | 75 |
| 同上 | `>= 5%` | 60 |
| 同上 | `>= 0%` | 45 |
| 同上 | `< 0%` | 20 |
| 当期利益 `Profit` | `> 0` | 65 |
| 同上 | `= 0` | 50 |
| 同上 | `< 0` | 25 |
| EPS `Eps` | `> 0` | 65 |
| 同上 | `= 0` | 50 |
| 同上 | `< 0` | 25 |

`Confidence` は利用できた項目数で決まります。

```text
Confidence = clamp(0.35 + FieldCount * 0.15, 0.35, 0.75)
```

財務キャッシュ自体がない場合は `RawScore = 50`, `Confidence = 0.10` として扱います。

## QualityScore

`FinancialSnapshot` がある場合、財務健全性と利益品質を点数化し、その平均を `RawScore` にします。利用可能な項目がない場合は `MissingData` に `quality` を追加し、中立値50を低confidenceで使います。

```text
QualityScore.Raw =
  Average(EquityRatioScore, NetAssetsRatioScore, ProfitQualityScore)
```

利用可能な項目だけを平均します。

| 評価項目 | 条件 | 点数 |
| --- | --- | --- |
| 自己資本比率 `EquityRatio` | `>= 55%` | 90 |
| 同上 | `>= 40%` | 75 |
| 同上 | `>= 25%` | 55 |
| 同上 | `>= 10%` | 35 |
| 同上 | `< 10%` | 20 |
| 純資産比率 `NetAssets / TotalAssets` | `>= 55%` | 90 |
| 同上 | `>= 40%` | 75 |
| 同上 | `>= 25%` | 55 |
| 同上 | `>= 10%` | 35 |
| 同上 | `< 10%` | 20 |
| 利益品質 | `OperatingProfit > 0` かつ `Profit > 0` | 75 |
| 同上 | `Profit < 0` | 25 |
| 同上 | その他 | 50 |

`EquityRatio` は `1` より大きい値ならパーセント表記として扱い、100で割って評価します。`Confidence` は `FundamentalScore` と同じく、利用できた項目数で決まります。

## MomentumScore

```text
MomentumScore =
  ReturnTrendScore * 0.40
+ MovingAvgScore   * 0.25
+ TechnicalScore   * 0.25
+ VolumeScore      * 0.10
```

日足が30本未満の場合は `MissingData` に `momentum` を追加し、中立値に近いスコアで扱います。

`ReturnTrendScore` は21日、63日、126日のリターンを点数化して加重平均します。

```text
ReturnTrendScore =
  ReturnScore(21日)  * 0.30
+ ReturnScore(63日)  * 0.40
+ ReturnScore(126日) * 0.30
```

| リターン | 点数 |
| --- | --- |
| データ不足 | 50 |
| `>= 20%` | 90 |
| `>= 10%` | 75 |
| `>= 0%` | 60 |
| `>= -10%` | 40 |
| `< -10%` | 20 |

`MovingAvgScore` は移動平均線との位置関係を25点ずつ加算します。日足が200本未満で加点が0の場合は、トレンド不明として50点を返します。

| 条件 | 加点 |
| --- | --- |
| 最新終値 `>` 25日SMA | +25 |
| 最新終値 `>` 75日SMA | +25 |
| 25日SMA `>` 75日SMA | +25 |
| 75日SMA `>` 200日SMA | +25 |

`TechnicalScore` はRSI、MACD、ADX相当の固定値で構成します。

```text
TechnicalScore =
  RsiScore  * 0.35
+ MacdScore * 0.35
+ AdxScore  * 0.30
```

現在の実装では `AdxScore` は未算出で、固定50です。

| RSI | 点数 |
| --- | --- |
| データ不足 | 50 |
| `45 <= RSI <= 65` | 80 |
| `35 <= RSI < 45` | 60 |
| `65 < RSI <= 75` | 60 |
| `RSI > 75` | 35 |
| `RSI < 30` | 30 |
| その他 | 45 |

| MACD | 点数 |
| --- | --- |
| 日足35本未満、またはシグナル算出不可 | 50 |
| MACD `>` シグナル | 70 |
| MACD `> 0` | 55 |
| その他 | 35 |

`VolumeScore` は直近20日の平均出来高に対する最新出来高で評価します。

| 条件 | 点数 |
| --- | --- |
| 日足20本未満、または平均出来高が0以下 | 50 |
| 最新出来高 `>` 平均の1.5倍 | 80 |
| 最新出来高 `>` 平均 | 65 |
| 最新出来高 `<` 平均の0.5倍 | 35 |
| その他 | 50 |

`Confidence` は日足本数で決まります。

| 日足本数 | Confidence |
| --- | --- |
| `>= 200` | 0.95 |
| `>= 75` | 0.75 |
| `>= 30` | 0.50 |
| `< 30` | 0.15 |

## NewsScore

```text
NewsScore =
  SentimentScore * 0.60
+ RelevanceScore * 0.25
+ EventRiskScore * 0.15
```

ニュースがない場合は `MissingData` に `news` を追加し、中立値で扱います。

ニュースがある場合は、各ニュースの `SentimentScore`、`RelevanceScore`、時間減衰を使って加重平均sentimentを作ります。

```text
WeightedSentiment =
  sum(SentimentScore * RelevanceScore * TimeDecay)
  / sum(max(0.05, RelevanceScore * TimeDecay))
```

時間減衰はニュースの経過日数で決まります。公開日時がない場合は `0.4` として扱います。

| 経過日数 | TimeDecay |
| --- | --- |
| `<= 3日` | 1.0 |
| `<= 7日` | 0.7 |
| `<= 14日` | 0.4 |
| `> 14日` | 0.2 |
| 公開日時なし | 0.4 |

| WeightedSentiment | SentimentScore |
| --- | --- |
| `>= 0.35` | 90 |
| `>= 0.15` | 75 |
| `>= -0.15` | 50 |
| `>= -0.35` | 30 |
| `< -0.35` | 15 |

`RelevanceScore` はニュースの平均relevanceを0から100に換算します。

```text
RelevanceScore = clamp(average(News.RelevanceScore) * 100, 0, 100)
```

`EventRiskScore` は加重平均sentimentから決めます。

| WeightedSentiment | EventRiskScore |
| --- | --- |
| `<= -0.35` | 20 |
| `< -0.15` | 40 |
| `> 0.20` | 85 |
| その他 | 70 |

`Confidence` はニュース件数で決まります。

```text
Confidence = clamp(0.35 + NewsCount * 0.05, 0.35, 0.85)
```

## PositionRiskScore

```text
PositionRiskScore =
  UnrealizedPnLScore * 0.25
+ ConcentrationScore * 0.25
+ VolatilityScore    * 0.25
+ LiquidityScore     * 0.15
+ CurrencyRiskScore  * 0.10
```

日本株限定のため、`CurrencyRiskScore` は固定で70です。為替データによる分岐はありません。

`Holding` の場合は、含み損益、集中度、日足由来のボラティリティと流動性を使います。`Watchlist` の場合は保有ポジションがないため、以下の固定値ベースで暫定評価します。

```text
Watchlist PositionRiskScore.Raw =
  70 * 0.25
+ 80 * 0.25
+ 50 * 0.25
+ 50 * 0.15
+ CurrencyRiskScore * 0.10
```

`CurrencyRiskScore` は固定70なので、`Watchlist` の `RawScore` は64.5、`Confidence` は0.45です。

`UnrealizedPnLScore` は含み損益率で評価します。含み損益率は、評価額と評価損益があればそれを優先し、なければ平均取得単価と現在価格から計算します。

```text
UnrealizedProfitLossRate =
  UnrealizedProfitLoss / (MarketValue - UnrealizedProfitLoss) * 100
```

または:

```text
UnrealizedProfitLossRate =
  (CurrentPrice - AverageAcquisitionPrice) / AverageAcquisitionPrice * 100
```

| 含み損益率 | 点数 |
| --- | --- |
| `>= 20%` | 75 |
| `>= 5%` | 85 |
| `>= -5%` | 70 |
| `>= -10%` | 45 |
| `>= -20%` | 25 |
| `< -20%` | 10 |
| 算出不可 | 50 |

`ConcentrationScore` は、銘柄評価額が日本株保有全体に占める比率で評価します。

| 評価額比率 | 点数 |
| --- | --- |
| 算出不可 | 50 |
| `< 5%` | 90 |
| `< 10%` | 75 |
| `< 20%` | 50 |
| `< 30%` | 25 |
| `>= 30%` | 10 |

`VolatilityScore` は直近61本の日足から日次リターンの標準偏差を計算します。日足61本未満の場合は `MissingData` に `volatility` を追加し、50点とします。

| 日次volatility | 点数 |
| --- | --- |
| `<= 1.0%` | 90 |
| `<= 2.0%` | 75 |
| `<= 3.5%` | 60 |
| `<= 5.5%` | 35 |
| `> 5.5%` | 15 |

`LiquidityScore` は直近20本の日足から平均売買代金を計算します。日足20本未満の場合は `MissingData` に `liquidity` を追加し、50点とします。

```text
AverageTradingValue = average(Close * Volume)
```

| 平均売買代金 | 点数 |
| --- | --- |
| `>= 100億円` | 90 |
| `>= 10億円` | 75 |
| `>= 1億円` | 60 |
| `>= 2,000万円` | 40 |
| `< 2,000万円` | 25 |

`Holding` の `Confidence` は利用できる保有情報と日足本数で決まります。

```text
Confidence = 0.55
+ 含み損益率が算出できる場合 0.15
+ 日足が60本以上ある場合 0.20
```

最終的に `0.30` から `0.95` の範囲に丸めます。

## 判定ルール

### Watchlist

| 条件 | Decision |
| --- | --- |
| `TotalScore >= 70` かつ `MomentumScore >= 55` かつ `PositionRiskScore >= 50` かつ `NewsScore >= 40` | `NewBuy` |
| `TotalScore >= 70` かつ `NewsScore < 40` | `Skip` |
| `TotalScore >= 55` | `Skip` |
| その他 | `Skip` |

### Holding

| 条件 | Decision | SellReasonType |
| --- | --- | --- |
| `UnrealizedProfitLossRate >= 20%` かつ `MomentumScore < 50` | `TakeProfit` | `TakeProfit` |
| `UnrealizedProfitLossRate >= 50%` かつ `NewsScore < 50` | `TakeProfit` | `TakeProfit` |
| `UnrealizedProfitLossRate <= -10%` かつ `TotalScore < 50` | `StopLoss` | `StopLoss` |
| `UnrealizedProfitLossRate <= -15%` かつ `MomentumScore < 45` | `StopLoss` | `StopLoss` |
| `TotalScore >= 55` かつ `PositionRiskScore >= 40` | `Hold` | `None` |
| その他 | `Hold` | `None` |

## MissingData

主な不足データキー:

- `fundamental`
- `quality`
- `momentum`
- `news`
- `daily`
- `market`
- `volatility`
- `liquidity`

不足データがあってもBot全体は停止せず、中立値とwarningでレポート生成を継続します。

## 分析結果の保存

`/report` の1回の実行は `AnalysisRun` として保存されます。各銘柄の `AnalysisResult` はその `AnalysisRun` に紐付き、最終スコアや判定に加えて以下をJSON文字列として保持します。

| 項目 | 内容 |
| --- | --- |
| `ScoreDetailsJson` | 各サブスコアの `ScoreBreakdown`、総合スコア、含み損益率、理由、warning、最終判断 |
| `InputDataSummaryJson` | 銘柄ID、銘柄コード、対象種別、価格・評価額の有無、日足件数、ニュース件数、財務データ有無、不足データ、通貨 |

JSONは追跡性を上げるための履歴情報で、現時点ではスコア計算ロジックの入力として再利用しません。
