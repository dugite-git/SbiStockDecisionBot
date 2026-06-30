# スコアリング仕様

このドキュメントは、Botが保有銘柄・ウォッチリスト銘柄をどのようにスコアリングし、最終的な `BotDecision` を決めるかを説明します。

このBotは投資助言や自動売買を行うものではありません。SBI CSV、ウォッチリスト、外部データキャッシュをもとに、判断材料を整理するための補助レポートを生成します。

## 対象

| TargetType | 対象 | 主な用途 |
| --- | --- | --- |
| `Holding` | SBI CSVから取り込んだ保有銘柄 | 保有継続、利確候補、損切り候補、リスク確認 |
| `Watchlist` | `/watch add` で登録した未保有銘柄 | 新規買い候補、監視継続、見送り |

保有中の銘柄と同じ銘柄がウォッチリストにもある場合、レポートでは保有銘柄として扱います。

## 入力データ

主な入力は `AnalysisInput` です。

| 項目 | 内容 |
| --- | --- |
| `SecurityId` | DB上の銘柄ID |
| `Symbol` | Bot上の銘柄コード |
| `Name` | 銘柄名 |
| `TargetType` | `Holding` または `Watchlist` |
| `Quantity` | 保有数量 |
| `AverageAcquisitionPrice` | 平均取得単価 |
| `CurrentPrice` | 現在価格 |
| `MarketValue` | 評価額 |
| `UnrealizedProfitLoss` | 評価損益 |
| `DailyPrices` | Alpha Vantage `TIME_SERIES_DAILY` 由来の日足 |
| `News` | Alpha Vantage `NEWS_SENTIMENT` 由来のニュース感情 |
| `TotalPortfolioMarketValue` | 保有銘柄全体の評価額 |
| `Currency` | 通貨 |
| `MissingData` | 不足しているデータ種別 |

## 総合スコア

すべてのサブスコアは `0` から `100` の範囲で扱います。高いほど良い評価です。

```text
TotalScore =
  FundamentalScore  * 0.40
+ QualityScore      * 0.20
+ MomentumScore     * 0.20
+ NewsScore         * 0.10
+ PositionRiskScore * 0.10
```

現在は第1段階の実装です。

| スコア | 現在の実装 |
| --- | --- |
| `FundamentalScore` | 財務系キャッシュが不足している場合は中立値 `50`、低confidence |
| `QualityScore` | 財務系キャッシュが不足している場合は中立値 `50`、低confidence |
| `MomentumScore` | 日足からリターン、移動平均、RSI、MACD、出来高を計算 |
| `NewsScore` | ニュース感情と関連度から計算。データなしなら中立値 |
| `PositionRiskScore` | 含み損益、集中度、ボラティリティ、流動性、為替リスクから計算 |

## Confidence補正

データが不足している銘柄で極端な高評価・低評価にならないよう、各サブスコアはconfidenceで補正します。

```text
AdjustedScore = RawScore * Confidence + 50 * (1 - Confidence)
```

例:

| RawScore | Confidence | AdjustedScore |
| ---: | ---: | ---: |
| 80 | 1.00 | 80 |
| 80 | 0.50 | 65 |
| 80 | 0.10 | 53 |

財務系キャッシュが不足している場合、`FundamentalScore` と `QualityScore` は `RawScore=50`、低confidenceとして扱います。

## MomentumScore

`TIME_SERIES_DAILY` のキャッシュがある場合に計算します。日足が30本未満の場合は中立値に近いスコアとなり、`MissingData` に `momentum` が追加されます。

```text
MomentumScore =
  ReturnTrendScore * 0.40
+ MovingAvgScore   * 0.25
+ TechnicalScore   * 0.25
+ VolumeScore      * 0.10
```

### ReturnTrendScore

1か月、3か月、6か月のリターンを重み付けします。

```text
ReturnTrendScore =
  1MonthReturnScore * 0.30
+ 3MonthReturnScore * 0.40
+ 6MonthReturnScore * 0.30
```

リターンの評価例:

| リターン | スコア |
| ---: | ---: |
| `20%` 以上 | 90 |
| `10%` 以上 | 75 |
| `0%` 以上 | 60 |
| `-10%` 以上 | 40 |
| `-10%` 未満 | 20 |
| 取得不可 | 50 |

### MovingAvgScore

条件を満たすごとに25点を加算します。

```text
現在値 > SMA25  -> +25
現在値 > SMA75  -> +25
SMA25 > SMA75   -> +25
SMA75 > SMA200  -> +25
```

### TechnicalScore

現在はRSIとMACDを中心に評価し、ADXは中立値として扱います。

```text
TechnicalScore =
  RSIScore  * 0.35
+ MACDScore * 0.35
+ ADXScore  * 0.30
```

RSIの評価例:

| RSI | スコア |
| ---: | ---: |
| 45から65 | 80 |
| 35から45未満 | 60 |
| 65超から75 | 60 |
| 75超 | 35 |
| 30未満 | 30 |

### VolumeScore

直近出来高と20日平均出来高を比較します。

| 条件 | スコア |
| --- | ---: |
| 直近出来高 > 20日平均の1.5倍 | 80 |
| 直近出来高 > 20日平均 | 65 |
| 直近出来高 < 20日平均の0.5倍 | 35 |
| その他 | 50 |

## NewsScore

Alpha Vantage `NEWS_SENTIMENT` のキャッシュがある場合に計算します。

```text
NewsScore =
  SentimentScore * 0.60
+ RelevanceScore * 0.25
+ EventRiskScore * 0.15
```

ニュースがない場合は中立値 `50` と低confidenceで扱い、`MissingData` に `news` が追加されます。

ニュースごとに以下を考慮します。

- `sentiment_score`
- `relevance_score`
- 公開日時に応じた時間減衰

時間減衰:

| 経過日数 | 係数 |
| ---: | ---: |
| 3日以内 | 1.0 |
| 7日以内 | 0.7 |
| 14日以内 | 0.4 |
| 15日以上 | 0.2 |

## PositionRiskScore

保有銘柄では、現在のポジションを持ち続けるリスクを評価します。高いほど安全寄りです。

```text
PositionRiskScore =
  UnrealizedPnLScore * 0.25
+ ConcentrationScore * 0.25
+ VolatilityScore    * 0.25
+ LiquidityScore     * 0.15
+ CurrencyRiskScore  * 0.10
```

ウォッチリスト銘柄はポジションを持たないため、含み損益と集中度に固定値を使い、極端な減点を避けます。

### UnrealizedPnLScore

| 含み損益率 | スコア |
| ---: | ---: |
| `+20%` 以上 | 75 |
| `+5%` 以上 | 85 |
| `-5%` 以上 | 70 |
| `-10%` 以上 | 45 |
| `-20%` 以上 | 25 |
| `-20%` 未満 | 10 |
| 取得不可 | 50 |

### ConcentrationScore

```text
positionRatio = 銘柄評価額 / 保有銘柄全体の評価額
```

| positionRatio | スコア |
| ---: | ---: |
| 5%未満 | 90 |
| 10%未満 | 75 |
| 20%未満 | 50 |
| 30%未満 | 25 |
| 30%以上 | 10 |
| 取得不可 | 50 |

### VolatilityScore

直近60日程度の日次リターン標準偏差を使います。

| 日次ボラティリティ | スコア |
| ---: | ---: |
| 1%以下 | 90 |
| 2%以下 | 75 |
| 3.5%以下 | 60 |
| 5.5%以下 | 35 |
| 5.5%超 | 15 |
| 取得不可 | 50 |

### LiquidityScore

20日平均売買代金を使います。

| 20日平均売買代金 | スコア |
| ---: | ---: |
| 100億以上 | 90 |
| 10億以上 | 75 |
| 1億以上 | 60 |
| 2,000万以上 | 40 |
| 2,000万未満 | 25 |
| 取得不可 | 50 |

### CurrencyRiskScore

JPY建て銘柄は原則 `70` とします。USD建て銘柄は為替データが未取得なら `50`、取得済みなら現時点では `60` として扱います。

## 判定ルール

### Watchlist

| 条件 | Decision |
| --- | --- |
| `TotalScore >= 70` かつ `MomentumScore >= 55` かつ `PositionRiskScore >= 50` かつ `NewsScore >= 40` | `NewBuy` |
| `TotalScore >= 70` だが `NewsScore < 40` | `Skip`、理由は監視継続 |
| `TotalScore >= 55` | `Skip`、理由は監視継続 |
| その他 | `Skip` |

現行のenumには `Watch` がないため、監視継続は `Skip` に理由文を付けて表現します。

### Holding

上から順に評価します。

| 条件 | Decision | SellReasonType |
| --- | --- | --- |
| `UnrealizedProfitLossRate >= 20%` かつ `MomentumScore < 50` | `TakeProfit` | `TakeProfit` |
| `UnrealizedProfitLossRate >= 50%` かつ `NewsScore < 50` | `TakeProfit` | `TakeProfit` |
| `UnrealizedProfitLossRate <= -10%` かつ `TotalScore < 50` | `StopLoss` | `StopLoss` |
| `UnrealizedProfitLossRate <= -15%` かつ `MomentumScore < 45` | `StopLoss` | `StopLoss` |
| `TotalScore >= 55` かつ `PositionRiskScore >= 40` | `Hold` | `None` |
| その他 | `Hold`、理由は注意付き保有 | `None` |

## RiskAlert相当

現行のenumには `RiskAlert` がないため、以下の条件に該当する場合はDecisionを置き換えず、理由文に警告として追記します。

```text
NewsScore <= 25
or PositionRiskScore <= 25
or PositionRisk raw score <= 25
```

## MissingData

不足データは `MissingData` とレポートのwarningに出力します。主な値は次の通りです。

- `fundamental`
- `quality`
- `momentum`
- `news`
- `daily`
- `market`
- `volatility`
- `liquidity`

不足データがあってもBot全体は停止せず、中立値とconfidence補正でレポート生成を継続します。
