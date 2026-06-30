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

## MomentumScore

```text
MomentumScore =
  ReturnTrendScore * 0.40
+ MovingAvgScore   * 0.25
+ TechnicalScore   * 0.25
+ VolumeScore      * 0.10
```

日足が30本未満の場合は `MissingData` に `momentum` を追加し、中立値に近いスコアで扱います。

## NewsScore

```text
NewsScore =
  SentimentScore * 0.60
+ RelevanceScore * 0.25
+ EventRiskScore * 0.15
```

ニュースがない場合は `MissingData` に `news` を追加し、中立値で扱います。

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
