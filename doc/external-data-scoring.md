# 外部市場データとスコアリング

このドキュメントは、外部市場データProviderから取得したデータをレポート生成とスコアリングでどう扱うかを説明します。既定はJ-Quants Providerで、日本株4桁コード向けのJ-Quants/EDINET/GDELTキャッシュ取得を標準で有効化します。

## 基本方針

- 対象銘柄は日本株4桁コードのみです。
- `/report` は外部APIを直接呼ばず、保存済みキャッシュとSBI CSV由来データを使います。
- 外部データが不足していてもレポート生成は止めず、中立スコアとwarningで扱います。
- Provider固有のAPI名、TTL、レート制限はProvider実装内に閉じ込めます。
- `ExternalApiCacheEntries` と `ExternalApiRequestLogs` はProvider名で分離して保存します。

## 設定

```env
MARKET_DATA_PROVIDER=JQuants
JQUANTS_API_KEY=
JQUANTS_BASE_URL=https://api.jquants.com
JQUANTS_RATE_LIMIT_PER_MINUTE=5
JQUANTS_FREE_DELAY_WEEKS=12

EDINET_API_KEY=
EDINET_BASE_URL=https://api.edinet-fsa.go.jp/api/v2
EDINET_LOOKBACK_DAYS=3

GDELT_BASE_URL=https://api.gdeltproject.org/api/v2/doc/doc
GDELT_TIMESPAN=7d
GDELT_MAX_RECORDS_PER_SECURITY=50
GDELT_REQUEST_DELAY_MS=1000
```

未設定の場合は `JQuants` として扱われます。外部市場データ取得なしにしたい場合は `Null` または `Disabled` を明示してください。別APIを追加する場合はInfrastructure層でProviderを登録し、対応するProvider名を設定してください。未対応のProvider名は起動時にエラーになります。

## Discordコマンド

### `/marketdata status`

Providerの取得状況を表示します。使用数、上限、残数、使用率、未取得キュー、次の取得候補を表示し、`prefetch` と `coverage` への導線を出します。NullProviderでは上限、使用数、残数、未取得キューはいずれも0です。

### `/marketdata coverage`

保有銘柄とウォッチリスト銘柄について、以下のカバレッジを表示します。

- 外部シンボル解決済みか
- 価格キャッシュがあるか
- 日足キャッシュがあるか
- ニュースキャッシュがあるか
- 為替キャッシュがあるか
- 解決エラーがあるか

Discord UIでは不足あり銘柄を優先表示し、10件単位のページングと不足のみ表示に対応します。

### `/marketdata prefetch limit:<n>`

Providerが実装されている場合に未取得データを事前取得します。Discord UIでは処理中表示の後、成功、失敗、スキップ、残りリクエスト、失敗優先ログを表示します。J-Quants Providerでは保有銘柄とウォッチリストの日本株4桁コードだけを対象に、J-Quantsの日足・財務、EDINET開示、GDELTニュースをキャッシュします。NullProviderでは取得せず、無効メッセージと空のリクエストログを返します。

## キャッシュテーブル

### `ExternalApiCacheEntries`

外部APIレスポンス本体を保存します。

| 項目 | 内容 |
| --- | --- |
| `Provider` | Provider名 |
| `Function` | Provider内の機能名 |
| `CacheKey` | 外部シンボルや通貨ペアなど |
| `PayloadJson` | レスポンスJSON |
| `FetchedAt` | 取得日時 |
| `ExpiresAt` | キャッシュ有効期限 |
| `Succeeded` | 成功レスポンスかどうか |
| `ErrorMessage` | 失敗時の理由 |

`Provider + Function + CacheKey` は一意です。

### `ExternalApiRequestLogs`

実際に外部APIへリクエストを送った履歴を保存します。

| 項目 | 内容 |
| --- | --- |
| `Provider` | Provider名 |
| `Function` | Provider内の機能名 |
| `CacheKey` | 外部シンボルや通貨ペアなど |
| `RequestedAt` | リクエスト日時 |
| `Succeeded` | 成功したか |
| `ErrorMessage` | 失敗時の理由 |

## 外部シンボル

Provider用に解決したシンボルは `Security` に保存します。

| 項目 | 内容 |
| --- | --- |
| `ExternalSymbol` | 解決済み外部シンボル |
| `ExternalSymbolResolvedAt` | 解決日時 |
| `ExternalSymbolResolutionError` | 解決失敗時のエラー |

旧DBのベンダー固有シンボル列はmigrationで上記の汎用列にリネームします。

## `/report` で使うデータ

1. SBI CSV由来の保有データを読む
2. アクティブなウォッチリストを読む
3. 日本株4桁コード以外を対象外にする
4. 同日成功済みの `MarketPriceSnapshots` があれば現在価格として使う
5. `DailyPrices` と `News` のキャッシュがあればスコア入力に渡す
6. 不足データは `MissingData` とwarningに記録し、中立値で計算する

Discord UIでは重要判断サマリと判断別件数を先に表示し、レポート全文はMarkdown添付で出力します。スコア計算とDB保存の内容は変わりません。

## スコアへの対応

| 外部データ | 主な用途 |
| --- | --- |
| 最新価格 | `CurrentPrice`, `MarketValue`, `UnrealizedProfitLoss`, `PositionRiskScore` |
| 日足OHLCV | `MomentumScore`, volatility, liquidity |
| ニュース | `NewsScore` |
| 財務・品質データ | `FundamentalScore`, `QualityScore` |

現在は外部AIによる補足分析を行いません。レポートとDB保存は `ScoreCalculator` と `BotDecisionResolver` のルールベース結果のみを使います。
