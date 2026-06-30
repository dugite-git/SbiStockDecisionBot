# SBI投資判断 Discord Bot

SBI証券の保有銘柄CSVと手動登録したウォッチリストをもとに、投資判断の参考レポートをDiscordへ出力する個人利用向けBotです。

このBotは自動売買Botではありません。SBI証券へのログイン、注文送信、信用取引、口座操作、パスワード保存は行いません。出力は投資判断の補助情報であり、最終判断は利用者本人が行う前提です。

## 技術スタック

- .NET 10 Worker Service
- Discord.Net
- Entity Framework Core
- SQLite
- Docker Compose
- Alpha Vantage

## セットアップ

`.env.example` をコピーして `.env` を作成し、Discord Botや外部APIの設定を入力します。

```powershell
Copy-Item .env.example .env
notepad .env
```

主な設定項目:

```env
DISCORD_TOKEN=your_discord_bot_token
DISCORD_GUILD_ID=123456789012345678
DISCORD_CHANNEL_ID=123456789012345678

DATABASE_PROVIDER=Sqlite
DATABASE_PATH=data/investment-decision-bot.db

TIME_ZONE=Asia/Tokyo
REPORT_TIME=08:00

OPENAI_ENABLED=false
OPENAI_API_KEY=
OPENAI_MODEL=gpt-4.1-mini

MARKET_DATA_ENABLED=false
MARKET_DATA_PROVIDER=
ALPHAVANTAGE_API_KEY=
ALPHAVANTAGE_FETCH_ON_REPORT=false
ALPHAVANTAGE_DAILY_REQUEST_LIMIT=25
ALPHAVANTAGE_MIN_REQUEST_INTERVAL_MS=1000
```

`ALPHAVANTAGE_MIN_REQUEST_INTERVAL_MS=1000` により、Alpha VantageへのHTTPリクエスト開始間隔を最低1秒空けます。無料キーの秒間バースト制限に合わせるため、`/marketdata prefetch` でも連続取得は1秒に1件ずつ進みます。

設定の読み込み優先順位は次の通りです。下にあるものほど優先されます。

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. `.env`
4. `.env.local`
5. OS環境変数

`.env` と `.env.local` はGit管理対象外です。実際のDiscord tokenやAPI keyはコミットしないでください。

## ローカル実行

```powershell
dotnet tool restore
dotnet run --project src/InvestmentDecisionBot.Worker
```

開発用にインメモリDBで起動する場合:

```powershell
$env:DOTNET_ENVIRONMENT="Development"
dotnet run --project src/InvestmentDecisionBot.Worker
```

`.env` で明示的にインメモリDBを使う場合:

```env
DATABASE_PROVIDER=InMemory
INMEMORY_DATABASE_NAME=investment-decision-bot-debug
```

## Docker Compose

```powershell
docker compose up --build -d
```

Docker環境では通常 `DATABASE_PROVIDER=Sqlite` を使い、SQLite DBはDocker volume `bot-data` に保存されます。

## Slash Commands

- `/import file:<csv>`  
  SBI証券CSVを取り込み、保有銘柄を同期します。売却済みになった銘柄は検出し、必要に応じてウォッチリストへ追加します。

- `/watch add symbol:<symbol>`  
  銘柄をウォッチリストへ追加します。

- `/watch remove symbol:<symbol>`  
  銘柄をウォッチリストから外します。保有中の銘柄はレポート対象として残ります。

- `/watch list`  
  現在のウォッチリストを表示します。

- `/report`  
  投資判断レポートを即時生成します。デフォルトではAlpha Vantageへ新規リクエストを送らず、保存済みキャッシュとSBI CSV由来データを使って計算します。

- `/marketdata status`  
  Alpha Vantageの当日リクエスト使用数、残り件数、未取得キュー、次に取得される候補を表示します。

- `/marketdata coverage`  
  監視対象のうちAlpha Vantageでシンボル解決できている銘柄数と、価格・日足・ニュース・為替キャッシュの取得状況を表示します。

- `/marketdata prefetch`  
  Alpha Vantage無料枠の残り件数内で、未取得データを順番に取得してキャッシュします。レポート投稿は行いません。

- `/marketdata prefetch limit:<n>`  
  最大 `n` 件まで取得を試みます。ただし実際の取得数は `ALPHAVANTAGE_DAILY_REQUEST_LIMIT` の残り件数を超えません。

## Alpha Vantage運用

Alpha Vantage無料版はリクエスト数が限られるため、このBotはキャッシュ優先・バッチ取得型で動作します。

有効化する設定:

```env
MARKET_DATA_ENABLED=true
MARKET_DATA_PROVIDER=AlphaVantage
ALPHAVANTAGE_API_KEY=your_api_key
ALPHAVANTAGE_FETCH_ON_REPORT=false
ALPHAVANTAGE_DAILY_REQUEST_LIMIT=25
ALPHAVANTAGE_MIN_REQUEST_INTERVAL_MS=1000
```

推奨運用:

1. `/import` でSBI CSVを取り込む
2. 必要な銘柄を `/watch add` で追加する
3. `/marketdata status` で未取得件数を確認する
4. `/marketdata coverage` で銘柄ごとのカバレッジを確認する
5. `/marketdata prefetch` で少しずつデータをキャッシュする
6. `/report` でキャッシュ済みデータを使ったレポートを生成する

`ALPHAVANTAGE_FETCH_ON_REPORT=false` がデフォルトです。これにより、朝の自動レポートや手動 `/report` が無料枠を一気に使い切ることを防ぎます。

## 取得対象とキャッシュ

Alpha Vantageから取得する主なデータ:

- `SYMBOL_RESOLVE`: 日本株4桁コードは `.T`、米国株ティッカーはそのままAlpha Vantage用シンボルへ解決
- `GLOBAL_QUOTE`: 最新価格
- `TIME_SERIES_DAILY`: 日足OHLCV
- `NEWS_SENTIMENT`: ニュース感情
- `CURRENCY_EXCHANGE_RATE`: 為替

キャッシュTTLの初期値:

| API | TTL |
| --- | --- |
| `SYMBOL_RESOLVE` | APIリクエストなし |
| `GLOBAL_QUOTE` | 当日中 |
| `TIME_SERIES_DAILY` | 1日 |
| `NEWS_SENTIMENT` | 6時間 |
| `CURRENCY_EXCHANGE_RATE` | 1日 |
| 財務系API | 7日 |

取得キューの優先順位:

1. 未解決の `SYMBOL_RESOLVE`
2. 保有銘柄の `GLOBAL_QUOTE`
3. 保有銘柄の `TIME_SERIES_DAILY`
4. ウォッチリスト銘柄の `GLOBAL_QUOTE`
5. ウォッチリスト銘柄の `TIME_SERIES_DAILY`
6. `NEWS_SENTIMENT`
7. 財務系API

## スコアリング

総合スコアは次の重みで計算します。

```text
TotalScore =
  FundamentalScore * 0.40
+ QualityScore     * 0.20
+ MomentumScore    * 0.20
+ NewsScore        * 0.10
+ PositionRiskScore * 0.10
```

現在の実装では第1段階として以下を実装済みです。

- `MomentumScore`: 日足からリターン、SMA、RSI、MACD、出来高を評価
- `PositionRiskScore`: 含み損益、集中度、ボラティリティ、流動性、為替リスクを評価
- `NewsScore`: Alpha Vantage `NEWS_SENTIMENT` がある場合に感情と関連度を評価
- `FundamentalScore` / `QualityScore`: 財務系キャッシュが不足している場合は中立値を低confidenceで使用

データ不足時は極端な評価にならないよう、以下の補正式を使います。

```text
AdjustedScore = RawScore * Confidence + 50 * (1 - Confidence)
```

詳しくは [doc/scoring.md](doc/scoring.md) と [doc/external-data-scoring.md](doc/external-data-scoring.md) を参照してください。

## 開発

```powershell
dotnet build
dotnet test
dotnet tool run dotnet-ef migrations add <Name> --project src/InvestmentDecisionBot.Infrastructure --startup-project src/InvestmentDecisionBot.Worker --context BotDbContext --output-dir Persistence/Migrations
```

現在の主なテスト範囲:

- CSV parser
- Import
- Watchlist
- Scoring
- Report
- Alpha Vantage provider/cache
