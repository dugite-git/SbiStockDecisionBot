# SBI投資判断Discord Bot

個人利用向けの投資判断支援Discord Botです。SBI証券CSVから現在保有している個別株だけを同期し、Discordへ参考レポートを投稿します。

このBotは自動売買Botではありません。SBI証券へのログイン、注文発行、証券口座操作、取引暗証番号やパスワード保存は実装していません。資産管理Botでもないため、現金残高、投資信託、NISA枠、配当、税金、総資産推移などは扱いません。

## 技術スタック

- .NET 10 Worker Service
- Discord.Net
- Entity Framework Core
- SQLite
- Docker Compose

## セットアップ

Discord Developer PortalでBotを作成し、対象guildに招待してください。必要な設定値は `.env` または環境変数で指定します。

主な設定項目:

```text
DISCORD_TOKEN=...
DISCORD_GUILD_ID=...
DISCORD_CHANNEL_ID=...
DATABASE_PROVIDER=Sqlite
DATABASE_PATH=data/investment-decision-bot.db
TIME_ZONE=Asia/Tokyo
REPORT_TIME=08:00
OPENAI_API_KEY=
OPENAI_ENABLED=false
OPENAI_MODEL=gpt-4.1-mini
NEWS_ENABLED=false
FINANCIAL_DATA_ENABLED=false
MAX_REPORT_ITEMS=20
```

## .envの設定手順

ローカル実行では、Workerが `.env` と `.env.local` を自動で読み込みます。まずサンプルファイルをコピーして、Discord Botの設定値などを入力してください。

```powershell
Copy-Item .env.example .env
notepad .env
```

`.env` の例:

```text
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
```

個人環境だけで上書きしたい値は `.env.local` に書けます。`.env` と `.env.local` はgit管理対象外です。実際のtokenやAPIキーはコミットしないでください。

設定の読み込み優先順位は次の通りです。下にあるものほど優先されます。

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. `.env`
4. `.env.local`
5. OS環境変数

## ローカル実行

SQLiteを使って通常起動する場合:

```powershell
dotnet tool restore
dotnet run --project src/InvestmentDecisionBot.Worker
```

DockerやSQLiteファイルを使わずにデバッグする場合は、Development設定のまま起動します。`appsettings.Development.json` では `DATABASE_PROVIDER=InMemory` が設定されているため、プロセス終了時にDB内容は消えます。

```powershell
$env:DOTNET_ENVIRONMENT="Development"
dotnet run --project src/InvestmentDecisionBot.Worker
```

`.env` で明示的にInMemory DBへ切り替える場合:

```text
DATABASE_PROVIDER=InMemory
INMEMORY_DATABASE_NAME=investment-decision-bot-debug
```

一時的にPowerShellの環境変数で切り替える場合:

```powershell
$env:DATABASE_PROVIDER="InMemory"
$env:INMEMORY_DATABASE_NAME="investment-decision-bot-debug"
dotnet run --project src/InvestmentDecisionBot.Worker
```

## Docker Compose

Docker Composeはリポジトリ直下の `.env` を自動で読みます。`.env.example` を `.env` にコピーして値を入れたあと、次のコマンドで起動します。

```powershell
docker compose up --build -d
```

Docker環境では `DATABASE_PROVIDER=Sqlite` を使い、SQLite DBはDocker volume `bot-data` に保存されます。

## Slash Command

- `/import file:<csv>`: SBI CSVから株式保有一覧を同期します。
- `/watch add symbol:<symbol>`: Watchlistへ手動追加します。
- `/watch remove symbol:<symbol>`: Watchlistから外します。保有中銘柄は分析対象に残ります。
- `/watch list`: Watchlistを表示します。
- `/report`: 即時レポートを生成します。

## CSV取り込み

MVPでは株式セクションだけを取り込みます。投資信託、合計行、資産額、NISA枠などは保存しません。対応する主な列は、銘柄コード、銘柄名称、保有株数、売却注文中、取得単価、現在値、取得金額、評価額、評価損益です。

CSVはUTF-8 BOM、UTF-8、CP932/Shift_JISを読みます。CSV異常時はDBを更新せず、Discordにエラーを返します。

## スコアリング

一次判断はBot内ルールで行います。OpenAI分析は `OPENAI_ENABLED=true` と `OPENAI_API_KEY` がある場合だけ補助説明用に実行され、最終DecisionはBot側スコアリングを優先します。

外部市場データ、ニュース、財務データProviderはMVPではNull実装で、データ不足時は中立スコアを使います。

## 開発

```powershell
dotnet build
dotnet test
dotnet tool run dotnet-ef migrations add <Name> --project src/InvestmentDecisionBot.Infrastructure --startup-project src/InvestmentDecisionBot.Worker --context BotDbContext --output-dir Persistence/Migrations
```

現在のテスト範囲はCSV parser、import、watchlist、scoring、reportです。
