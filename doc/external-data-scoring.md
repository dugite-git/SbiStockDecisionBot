# 外部データ取得とスコアリング

このドキュメントは、Alpha Vantageから取得した外部データが、レポート生成とスコアリングにどう使われるかを説明します。

## 基本方針

Alpha Vantage無料版はリクエスト数が限られるため、このBotは以下の方針で動作します。

- APIレスポンスはDBにキャッシュする
- 同じデータはTTL内で再取得しない
- 当日のリクエスト数を `ExternalApiRequestLogs` で数える
- `ALPHAVANTAGE_DAILY_REQUEST_LIMIT` を超える取得は行わない
- `/report` は原則として外部APIを呼ばず、保存済みキャッシュを使う
- データ不足時もBot全体を止めず、中立スコアとwarningで処理を続ける

## 設定

Alpha Vantageを有効化する設定:

```env
MARKET_DATA_ENABLED=true
MARKET_DATA_PROVIDER=AlphaVantage
ALPHAVANTAGE_API_KEY=your_api_key
ALPHAVANTAGE_FETCH_ON_REPORT=false
ALPHAVANTAGE_DAILY_REQUEST_LIMIT=25
ALPHAVANTAGE_MIN_REQUEST_INTERVAL_MS=1000
```

`ALPHAVANTAGE_MIN_REQUEST_INTERVAL_MS=1000` はAlpha VantageへのHTTPリクエスト開始間隔を最低1秒空ける設定です。無料キーでは秒間バースト制限に当たりやすいため、通常はこの値のまま運用します。

`ALPHAVANTAGE_FETCH_ON_REPORT=false` が推奨です。これにより `/report` や定時レポートが無料枠を消費しにくくなります。

## Discordコマンド

### `/marketdata status`

Alpha Vantageの取得状況を表示します。

表示内容:

- 日次上限
- 今日の使用済みリクエスト数
- 今日の残りリクエスト数
- 未取得キュー件数
- 次に取得される候補

### `/marketdata prefetch`

未取得キューを日次残枠の範囲で進めます。レポート生成やDiscord投稿は行いません。

### `/marketdata coverage`

監視対象のAlpha Vantageカバレッジを表示します。保有銘柄とウォッチリスト銘柄を対象に、以下を集計します。

- Alpha Vantageシンボル解決済みか
- 価格キャッシュがあるか
- 日足キャッシュがあるか
- ニュース感情キャッシュがあるか
- 為替データがカバーされているか
- シンボル解決エラーがあるか

銘柄別の詳細はDiscordの文字数制限を避けるため、先頭20件まで表示します。

### `/marketdata prefetch limit:<n>`

最大 `n` 件まで取得を試みます。ただし、実際の取得数は今日の残りリクエスト数を超えません。

例:

```text
/marketdata prefetch limit:5
```

今日の残り枠が3件なら、実際に試みる取得は最大3件です。

## 取得キューの優先順位

取得対象は、アクティブな保有銘柄と、保有銘柄と重複しないアクティブなウォッチリスト銘柄です。

優先順位:

1. 未解決の `SYMBOL_RESOLVE`
2. 保有銘柄の `GLOBAL_QUOTE`
3. 保有銘柄の `TIME_SERIES_DAILY`
4. ウォッチリスト銘柄の `GLOBAL_QUOTE`
5. ウォッチリスト銘柄の `TIME_SERIES_DAILY`
6. `NEWS_SENTIMENT`
7. 財務系API

現在の実装では、第1段階として価格、日足、ニュース感情を主に使用します。財務系APIはキャッシュ基盤に合わせて後続段階で追加する想定です。

## キャッシュテーブル

### `ExternalApiCacheEntries`

Alpha VantageのAPIレスポンス本体を保存します。

主な項目:

| 項目 | 内容 |
| --- | --- |
| `Provider` | `AlphaVantage` |
| `Function` | `GLOBAL_QUOTE` などのAPI名 |
| `CacheKey` | シンボルや通貨ペアなど |
| `PayloadJson` | レスポンスJSON |
| `FetchedAt` | 取得日時 |
| `ExpiresAt` | キャッシュ有効期限 |
| `Succeeded` | 成功レスポンスかどうか |
| `ErrorMessage` | 失敗時の理由 |

`Provider + Function + CacheKey` は一意です。

### `ExternalApiRequestLogs`

実際にAlpha VantageへHTTPリクエストを送った履歴を保存します。日次リクエスト数の判定に使います。

主な項目:

| 項目 | 内容 |
| --- | --- |
| `Provider` | `AlphaVantage` |
| `Function` | `GLOBAL_QUOTE` などのAPI名 |
| `CacheKey` | シンボルや通貨ペアなど |
| `RequestedAt` | リクエスト日時 |
| `Succeeded` | 成功したか |
| `ErrorMessage` | 失敗時の理由 |

## TTL

初期TTL:

| API | TTL |
| --- | --- |
| `SYMBOL_RESOLVE` | APIリクエストなし |
| `GLOBAL_QUOTE` | 当日中 |
| `TIME_SERIES_DAILY` | 1日 |
| `NEWS_SENTIMENT` | 6時間 |
| `CURRENCY_EXCHANGE_RATE` | 1日 |
| 財務系API | 7日 |

失敗レスポンスは短時間だけキャッシュし、同じ失敗リクエストを連打しないようにします。

## シンボル解決

Bot上の銘柄コードをそのままAlpha Vantageへ渡すとは限りません。このBotは日本株と米国株のみを対象にし、Alpha Vantage用シンボルはローカルルールで解決します。シンボル解決ではAlpha Vantage APIリクエストを消費しません。

例:

| Bot上のsymbol | Alpha Vantage側の例 |
| --- | --- |
| `7203` | `7203.T` |
| `AAPL` | `AAPL` |
| `IBM` | `IBM` |

解決結果は `Security` に保存します。

| 項目 | 内容 |
| --- | --- |
| `AlphaVantageSymbol` | 解決済みシンボル |
| `AlphaVantageSymbolResolvedAt` | 解決日時 |
| `AlphaVantageSymbolResolutionError` | 解決失敗時のエラー |

候補の順位付けでは、完全一致、前方一致、通貨、国、Alpha Vantageの `matchScore`、株式種別をヒントとして使います。

## `/report` の外部データ利用

`/report` は次の順でデータを使います。

1. SBI CSV由来の保有データを読む
2. ウォッチリストを読む
3. 同日成功済みの `MarketPriceSnapshots` があれば現在価格として使う
4. `TIME_SERIES_DAILY` と `NEWS_SENTIMENT` のキャッシュがあればスコア入力に渡す
5. キャッシュがなければ中立値とwarningで処理を続ける

`ALPHAVANTAGE_FETCH_ON_REPORT=false` の場合、キャッシュがなくても `/report` はAlpha Vantageへ新規リクエストを送りません。

`ALPHAVANTAGE_FETCH_ON_REPORT=true` の場合のみ、価格キャッシュがない銘柄についてレポート実行中に取得を試みます。ただし、この設定は無料枠を消費しやすいため通常は推奨しません。

## 外部データとスコアの対応

| 外部データ | 保存先 | 主な用途 |
| --- | --- | --- |
| `SYMBOL_RESOLVE` | `Security.AlphaVantageSymbol` | 以後のAPI取得用シンボル解決 |
| `GLOBAL_QUOTE` | `MarketPriceSnapshots`, `ExternalApiCacheEntries` | 現在価格、評価額、含み損益 |
| `TIME_SERIES_DAILY` | `ExternalApiCacheEntries` | Momentum、ボラティリティ、流動性 |
| `NEWS_SENTIMENT` | `ExternalApiCacheEntries` | NewsScore |
| `CURRENCY_EXCHANGE_RATE` | `ExternalApiCacheEntries` | 為替リスク |

## 価格データの影響

保有銘柄で外部価格が使える場合:

```text
CurrentPrice = 外部価格
MarketValue = CurrentPrice * Quantity
UnrealizedProfitLoss = MarketValue - AverageAcquisitionPrice * Quantity
```

これにより以下が変わります。

- `UnrealizedProfitLossRate`
- `PositionRiskScore`
- `TotalScore`
- `TakeProfit` / `StopLoss` などの判定

価格が取得できない場合は、可能な範囲でSBI CSV由来の価格や評価額を使います。

## 日足データの影響

`TIME_SERIES_DAILY` がある場合、以下に使います。

- 1か月、3か月、6か月リターン
- SMA25、SMA75、SMA200
- RSI
- MACD
- 出来高
- 60日ボラティリティ
- 20日平均売買代金

これにより、主に以下が変わります。

- `MomentumScore`
- `PositionRiskScore`
- 利確候補判定
- 損切り候補判定

## ニュースデータの影響

`NEWS_SENTIMENT` がある場合、以下を使って `NewsScore` を計算します。

- sentiment
- relevance
- 公開日時による時間減衰

ニュースがない場合は `NewsScore=50` 相当の中立値を低confidenceで扱います。

## データ不足時の扱い

外部データが不足しても、レポート生成は止めません。

不足データは `MissingData` とwarningに記録し、スコアは中立値とconfidence補正で計算します。

```text
AdjustedScore = RawScore * Confidence + 50 * (1 - Confidence)
```

この仕組みにより、データが少ない銘柄が極端な買い候補・売り候補になりにくくなります。

## 運用例

無料枠を使いすぎない運用例:

```text
朝:
  /marketdata status
  /marketdata prefetch limit:5

昼:
  /marketdata status
  /marketdata prefetch limit:5

夜:
  /marketdata status
  /report
```

保有銘柄・ウォッチリスト銘柄が多い場合、すべてのデータが1日で揃うとは限りません。数日かけてキャッシュを育てる前提で運用します。
