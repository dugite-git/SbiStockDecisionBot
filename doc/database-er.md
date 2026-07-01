# データベースER図

このドキュメントは、現在の `BotDbContext` / EF Core migration snapshot に基づくデータベース構造をまとめます。実DBはSQLiteを想定し、列型はEF CoreのSQLiteマッピングに従います。

## 全体像

```mermaid
erDiagram
    Securities ||--o| Holdings : "has active/current holding"
    Securities ||--o{ HoldingSnapshots : "has historical snapshots"
    Securities ||--o{ WatchlistItems : "is watched by"
    Securities ||--o{ SoldEvents : "has sold detections"
    Securities ||--o{ MarketPriceSnapshots : "has price snapshots"
    Securities ||--o{ NewsItems : "may relate to"
    Securities ||--o{ AnalysisResults : "is analyzed as"
    AnalysisResults ||--o{ AiAnalysisLogs : "may have AI logs"

    Securities {
        int Id PK
        string Symbol
        string Name
        string Market
        string Country
        string Currency
        string ExternalSymbol
        datetime ExternalSymbolResolvedAt
        string ExternalSymbolResolutionError
        int SecurityType
        datetime CreatedAt
        datetime UpdatedAt
    }

    Holdings {
        int Id PK
        int SecurityId FK
        decimal Quantity
        decimal PendingSellQuantity
        decimal AverageAcquisitionPrice
        decimal AcquisitionAmount
        decimal ImportedCurrentPrice
        decimal ImportedMarketValue
        decimal ImportedUnrealizedProfitLoss
        datetime ImportedAt
        bool IsActive
        datetime CreatedAt
        datetime UpdatedAt
    }

    HoldingSnapshots {
        int Id PK
        int SecurityId FK
        decimal Quantity
        decimal PendingSellQuantity
        decimal AverageAcquisitionPrice
        decimal AcquisitionAmount
        decimal ImportedCurrentPrice
        decimal ImportedMarketValue
        decimal ImportedUnrealizedProfitLoss
        date SnapshotDate
        string SourceCsvFileName
        datetime CreatedAt
    }

    WatchlistItems {
        int Id PK
        int SecurityId FK
        int Source
        bool IsActive
        datetime AddedAt
        datetime RemovedAt
        datetime CreatedAt
        datetime UpdatedAt
    }

    SoldEvents {
        int Id PK
        int SecurityId FK
        datetime DetectedAt
        decimal PreviousQuantity
        decimal PreviousAverageAcquisitionPrice
        decimal PreviousImportedCurrentPrice
        decimal PreviousImportedMarketValue
        decimal PreviousImportedUnrealizedProfitLoss
        string Reason
        datetime CreatedAt
    }

    MarketPriceSnapshots {
        int Id PK
        int SecurityId FK
        decimal Price
        string Currency
        datetime FetchedAt
        string DataSource
        bool IsStale
        bool UsedFallback
        string ErrorMessage
        datetime CreatedAt
    }

    NewsItems {
        int Id PK
        int SecurityId FK
        string Title
        string Url
        datetime PublishedAt
        string Source
        string Summary
        string Sentiment
        datetime FetchedAt
        datetime CreatedAt
    }

    AnalysisResults {
        int Id PK
        int SecurityId FK
        date AnalysisDate
        int TargetType
        decimal FundamentalScore
        decimal QualityScore
        decimal MomentumScore
        decimal NewsScore
        decimal PositionRiskScore
        decimal TotalScore
        int BotDecision
        int SellReasonType
        decimal Confidence
        string Reason
        string MissingData
        bool DecisionConflict
        datetime CreatedAt
    }

    AiAnalysisLogs {
        int Id PK
        int AnalysisResultId FK
        string RequestJson
        string ResponseJson
        string Model
        bool Succeeded
        string ErrorMessage
        datetime CreatedAt
    }

    ExternalApiCacheEntries {
        int Id PK
        string Provider
        string Function
        string CacheKey
        string PayloadJson
        datetime FetchedAt
        datetime ExpiresAt
        bool Succeeded
        string ErrorMessage
        datetime CreatedAt
        datetime UpdatedAt
    }

    ExternalApiRequestLogs {
        int Id PK
        string Provider
        string Function
        string CacheKey
        datetime RequestedAt
        bool Succeeded
        string ErrorMessage
    }

    DailyReports {
        int Id PK
        date ReportDate
        string Content
        bool PostedToDiscord
        string DiscordMessageId
        datetime GeneratedAt
        datetime PostedAt
        datetime CreatedAt
    }

    SystemLogs {
        int Id PK
        string Level
        string Category
        string Message
        string Exception
        datetime CreatedAt
    }
```

## テーブルの役割

| テーブル | 役割 |
| --- | --- |
| `Securities` | 銘柄マスタ。SBI CSVの銘柄コード、外部Provider向けシンボル、通貨・市場情報を保持します。 |
| `Holdings` | 現在の保有状態。1銘柄につき最大1行です。 |
| `HoldingSnapshots` | CSV取込時点の保有状態履歴。日次・取込ごとの記録です。 |
| `WatchlistItems` | 監視リスト登録状態。手動追加や自動登録の出どころを保持します。 |
| `SoldEvents` | 最新CSVから消えた銘柄など、売却検知イベントの履歴です。 |
| `MarketPriceSnapshots` | 外部Providerなどから取得した価格スナップショットです。 |
| `NewsItems` | 銘柄に紐づく、または市場全体に関係するニュースです。 |
| `AnalysisResults` | レポート生成時のスコアリング結果と最終判断です。 |
| `AiAnalysisLogs` | AI分析リクエスト/レスポンスのログです。現在の実装では補助的な履歴です。 |
| `ExternalApiCacheEntries` | 外部APIレスポンスJSONのキャッシュです。 |
| `ExternalApiRequestLogs` | 外部APIを実際に呼び出した履歴です。 |
| `DailyReports` | 生成した日次レポート本文とDiscord投稿状態です。 |
| `SystemLogs` | アプリケーション内のシステムログです。 |

## リレーション

| 親 | 子 | 関係 | 外部キー | 削除動作 |
| --- | --- | --- | --- | --- |
| `Securities` | `Holdings` | 1 対 0..1 | `Holdings.SecurityId` | Cascade |
| `Securities` | `HoldingSnapshots` | 1 対 多 | `HoldingSnapshots.SecurityId` | Cascade |
| `Securities` | `WatchlistItems` | 1 対 多 | `WatchlistItems.SecurityId` | Cascade |
| `Securities` | `SoldEvents` | 1 対 多 | `SoldEvents.SecurityId` | Cascade |
| `Securities` | `MarketPriceSnapshots` | 1 対 多 | `MarketPriceSnapshots.SecurityId` | Cascade |
| `Securities` | `NewsItems` | 1 対 0..多 | `NewsItems.SecurityId` | 既定動作 |
| `Securities` | `AnalysisResults` | 1 対 多 | `AnalysisResults.SecurityId` | Cascade |
| `AnalysisResults` | `AiAnalysisLogs` | 1 対 0..多 | `AiAnalysisLogs.AnalysisResultId` | 既定動作 |

`ExternalApiCacheEntries`、`ExternalApiRequestLogs`、`DailyReports`、`SystemLogs` は、現時点では他テーブルへの外部キーを持たない独立テーブルです。

## 主な制約とインデックス

| テーブル | 制約 / インデックス |
| --- | --- |
| `Securities` | `SecurityType + Symbol` に一意インデックス。`Symbol` は最大32文字、`Name` は最大256文字、`ExternalSymbol` は最大64文字。 |
| `Holdings` | `SecurityId` に一意インデックス。1銘柄につき現在保有は最大1行。数量・取得単価・評価額系は `decimal(18,4)`。 |
| `HoldingSnapshots` | `SecurityId` にインデックス。数量・取得単価・評価額系は `decimal(18,4)`。 |
| `WatchlistItems` | `SecurityId + IsActive` にインデックス。 |
| `SoldEvents` | `SecurityId` にインデックス。 |
| `MarketPriceSnapshots` | `SecurityId` にインデックス。 |
| `NewsItems` | `SecurityId` にインデックス。`SecurityId` はnullableで、市場全体ニュースも保存できます。 |
| `AnalysisResults` | `SecurityId + AnalysisDate + TargetType` にインデックス。 |
| `AiAnalysisLogs` | `AnalysisResultId` にインデックス。 |
| `ExternalApiCacheEntries` | `Provider + Function + CacheKey` に一意インデックス、`ExpiresAt` にインデックス。`Provider` / `Function` は最大64文字、`CacheKey` は最大256文字。 |
| `ExternalApiRequestLogs` | `Provider + RequestedAt` にインデックス。`Provider` / `Function` は最大64文字、`CacheKey` は最大256文字。 |
| `DailyReports` | `ReportDate` にインデックス。 |
| `SystemLogs` | `CreatedAt` にインデックス。 |

## Enum値

DB上では以下のenumは `INTEGER` として保存されます。

| Enum | 値 |
| --- | --- |
| `SecurityType` | `Unknown = 0`, `Stock = 1` |
| `TargetType` | `Holding = 0`, `Watchlist = 1` |
| `WatchlistSource` | `Manual = 0`, `SoldAutomatically = 1` |
| `BotDecision` | `BuyMore = 0`, `Hold = 1`, `PartialTakeProfit = 2`, `TakeProfit = 3`, `PartialStopLoss = 4`, `StopLoss = 5`, `AnalysisFailed = 6`, `NewBuy = 7`, `Skip = 8` |
| `SellReasonType` | `None = 0`, `TakeProfit = 1`, `StopLoss = 2`, `Rebalance = 3`, `FundamentalDeterioration = 4`, `RiskAvoidance = 5` |

## 補足

- 現在のスキーマ定義は `src/InvestmentDecisionBot.Infrastructure/Persistence/BotDbContext.cs` と `src/InvestmentDecisionBot.Infrastructure/Persistence/Migrations/BotDbContextModelSnapshot.cs` が基準です。
- `DateOnly` / `DateTimeOffset` はSQLiteでは `TEXT` として保存されます。
- `MarketPriceSnapshots.FetchedAt` は、当日再利用判定のLINQ日付範囲検索をSQLite側で実行できるよう、EF Coreで文字列変換を明示しています。
- EF Coreのnullable設定により、`SecurityId` がnullableでない子テーブルは必須リレーションです。`NewsItems.SecurityId` と `AiAnalysisLogs.AnalysisResultId` はnullableです。
