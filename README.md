# SBI Stock Decision Discord Bot

SBI証券の保有銘柄CSVと手動ウォッチリストをもとに、日本株の投資判断レポートをDiscordへ出力する個人利用向けBotです。自動売買、SBIへのログイン、注文、信用取引、口座操作、パスワード保存は行いません。

## 技術スタック

- .NET 10 Worker Service
- Discord.Net
- Entity Framework Core
- SQLite
- Docker Compose

## セットアップ

`.env.example` をコピーして `.env` を作成し、Discord Bot設定を入力します。

```powershell
Copy-Item .env.example .env
notepad .env
```

主な設定:

```env
DISCORD_TOKEN=your_discord_bot_token
DISCORD_GUILD_ID=123456789012345678
DISCORD_CHANNEL_ID=123456789012345678

DATABASE_PROVIDER=Sqlite
DATABASE_PATH=data/investment-decision-bot.db

TIME_ZONE=Asia/Tokyo
REPORT_TIME=08:00

MARKET_DATA_PROVIDER=JQuants
DISCLOSURE_PROVIDER=
NEWS_PROVIDER=

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
GDELT_REQUEST_DELAY_MS=6000
```

`MARKET_DATA_PROVIDER` は未設定の場合も `JQuants` として扱われ、外部市場データProviderを標準で有効化します。外部市場データ取得を止めたい場合は `Null` または `Disabled` を明示してください。別APIを追加する場合はInfrastructure層にProvider実装を登録してからProvider名を設定してください。未対応のProvider名を指定すると起動時に設定エラーになります。

J-Quantsの取得には `JQUANTS_API_KEY` を設定します。J-Quantsの無料データは遅延データとして扱い、保有銘柄の現在値はSBI CSVの値を優先します。EDINETは `EDINET_API_KEY` がある場合だけ開示書類一覧を補助取得し、GDELTはAPIキーなしでニュース補助取得に使います。

## 実行

```powershell
dotnet tool restore
dotnet run --project src/InvestmentDecisionBot.Worker
```

Docker:

```powershell
docker compose up --build -d
```

## 対象銘柄

このBotは日本株のみを対象にします。

- 銘柄コードは4桁数字のみ許可します。
- `symbol` 入力は前後空白を除去し、全角数字を半角へ変換し、末尾の `.T` を外して4桁コードとして扱います。
- `/watch add` は正規化後も4桁数字以外の場合に拒否します。
- SBI CSV取り込みでは4桁数字以外のコードをスキップします。
- 既存DBに米国株などが残っていても、レポート対象には含めません。

## Slash Commands

- `/import file:<csv>`: SBI証券CSVを取り込み、保有銘柄を同期します。結果は概要、ファイル情報、次の操作に分けて表示し、`/watch targets`、`/marketdata prefetch`、`/report` への導線ボタンを表示します。
- `/watch add symbol:<symbol>`: 日本株4桁コードをウォッチリストへ追加します。`symbol` はAutocompleteに対応し、全角数字や `.T` 付き入力も正規化します。
- `/watch remove symbol:<symbol>`: ウォッチリストから外します。保有中の銘柄はレポート対象として残ることを結果に表示します。
- `/watch list`: 現在のウォッチリストを登録元別サマリつきで表示します。10件単位のページングに対応します。
- `/watch targets`: 監視対象に入っている銘柄情報を表示します。外部シンボル未解決の件数を表示し、10件単位のページングと未解決のみ表示に対応します。
- `/report`: ルールベースの投資判断レポートを即時生成します。外部AI補足は使用しません。重要判断サマリ、判断別件数、不足データをEmbed先頭に表示し、全文Markdownを添付ファイルとして出力します。
- `/marketdata status`: 外部市場データProviderの取得状況を表示します。使用率、残りリクエスト、次の取得候補を表示し、事前取得とカバレッジへの導線を出します。
- `/marketdata coverage`: 外部シンボル、価格、日足、ニュース、為替のキャッシュ状況を表示します。不足あり銘柄を優先し、10件単位のページングと不足のみ表示に対応します。
- `/marketdata data symbol:<symbol>`: APIから取得して保存済みの価格、財務、ニュース、キャッシュ情報を銘柄別に表示します。`symbol` はAutocompleteに対応します。
- `/marketdata articles symbol:<symbol> [limit:<n>]`: APIから取得して保存済みの記事データを銘柄別に表示します。センチメント集計と平均関連度を表示し、5件単位のページングに対応します。
- `/marketdata prefetch [limit:<n>]`: Providerが実装されている場合に、未取得データの事前取得を試みます。処理中表示の後、成功、失敗、スキップ、残りリクエスト、失敗優先ログを表示します。現在の既定ProviderはJ-Quantsです。

## 外部市場データProvider

既定はJ-Quants Providerです。`MARKET_DATA_PROVIDER` が未設定、または `JQuants` の場合、日本株4桁コードだけを対象にJ-Quants、EDINET、GDELTの取得とキャッシュを行います。

- `IMarketDataProvider`: 最新価格取得
- `ICachedMarketDataProvider`: 日足、ニュース、為替などのキャッシュ参照
- `IMarketDataPrefetchService`: `/marketdata` のstatus、coverage、prefetch

`ExternalApiCacheEntries` と `ExternalApiRequestLogs` はProvider名、機能名、キャッシュキーで汎用的に保存します。`Security.ExternalSymbol` はProvider用に解決した外部シンボルを保存するための汎用列です。

`/report` は外部APIを直接呼びません。`/marketdata prefetch` で保存したキャッシュとSBI CSV由来データを使い、API失敗時も中立スコアとwarningで継続します。

## スコアリング

総合スコアは以下の重みで計算します。

```text
TotalScore =
  FundamentalScore   * 0.40
+ QualityScore       * 0.20
+ MomentumScore      * 0.20
+ NewsScore          * 0.10
+ PositionRiskScore  * 0.10
```

外部データが不足している項目は中立値とconfidence補正で扱い、Bot全体は停止しません。詳しくは `doc/scoring.md` と `doc/external-data-scoring.md` を参照してください。

## 開発

```powershell
dotnet build
dotnet test
dotnet tool run dotnet-ef migrations add <Name> --project src/InvestmentDecisionBot.Infrastructure --startup-project src/InvestmentDecisionBot.Worker --context BotDbContext --output-dir Persistence/Migrations
```

主なテスト対象:

- CSV parser
- Import
- Watchlist
- Scoring
- Report
- Market data provider contracts
