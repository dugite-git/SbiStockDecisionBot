using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestmentDecisionBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImportBatchAndAnalysisRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NewsItems_SecurityId",
                table: "NewsItems");

            migrationBuilder.DropIndex(
                name: "IX_MarketPriceSnapshots_SecurityId",
                table: "MarketPriceSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_HoldingSnapshots_SecurityId",
                table: "HoldingSnapshots");

            migrationBuilder.AddColumn<int>(
                name: "ImportBatchId",
                table: "SoldEvents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImportBatchId",
                table: "HoldingSnapshots",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AnalysisRunId",
                table: "DailyReports",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AnalysisRunId",
                table: "AnalysisResults",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InputDataSummaryJson",
                table: "AnalysisResults",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScoreDetailsJson",
                table: "AnalysisResults",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceCsvFileName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EncodingName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ImportedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SoldCount = table.Column<int>(type: "INTEGER", nullable: false),
                    WatchAddedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SkippedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Succeeded = table.Column<bool>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AnalysisRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AnalysisDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Trigger = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ImportBatchId = table.Column<int>(type: "INTEGER", nullable: true),
                    Succeeded = table.Column<bool>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnalysisRuns_ImportBatches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SoldEvents_ImportBatchId",
                table: "SoldEvents",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_NewsItems_SecurityId_PublishedAt",
                table: "NewsItems",
                columns: new[] { "SecurityId", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NewsItems_Source_PublishedAt",
                table: "NewsItems",
                columns: new[] { "Source", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NewsItems_Url",
                table: "NewsItems",
                column: "Url");

            migrationBuilder.CreateIndex(
                name: "IX_MarketPriceSnapshots_SecurityId_DataSource_FetchedAt",
                table: "MarketPriceSnapshots",
                columns: new[] { "SecurityId", "DataSource", "FetchedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketPriceSnapshots_SecurityId_FetchedAt",
                table: "MarketPriceSnapshots",
                columns: new[] { "SecurityId", "FetchedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HoldingSnapshots_ImportBatchId",
                table: "HoldingSnapshots",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_HoldingSnapshots_SecurityId_SnapshotDate",
                table: "HoldingSnapshots",
                columns: new[] { "SecurityId", "SnapshotDate" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyReports_AnalysisRunId",
                table: "DailyReports",
                column: "AnalysisRunId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisResults_AnalysisRunId",
                table: "AnalysisResults",
                column: "AnalysisRunId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisRuns_AnalysisDate_StartedAt",
                table: "AnalysisRuns",
                columns: new[] { "AnalysisDate", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisRuns_ImportBatchId",
                table: "AnalysisRuns",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_ImportedAt",
                table: "ImportBatches",
                column: "ImportedAt");

            migrationBuilder.AddForeignKey(
                name: "FK_AnalysisResults_AnalysisRuns_AnalysisRunId",
                table: "AnalysisResults",
                column: "AnalysisRunId",
                principalTable: "AnalysisRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_DailyReports_AnalysisRuns_AnalysisRunId",
                table: "DailyReports",
                column: "AnalysisRunId",
                principalTable: "AnalysisRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_HoldingSnapshots_ImportBatches_ImportBatchId",
                table: "HoldingSnapshots",
                column: "ImportBatchId",
                principalTable: "ImportBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_SoldEvents_ImportBatches_ImportBatchId",
                table: "SoldEvents",
                column: "ImportBatchId",
                principalTable: "ImportBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AnalysisResults_AnalysisRuns_AnalysisRunId",
                table: "AnalysisResults");

            migrationBuilder.DropForeignKey(
                name: "FK_DailyReports_AnalysisRuns_AnalysisRunId",
                table: "DailyReports");

            migrationBuilder.DropForeignKey(
                name: "FK_HoldingSnapshots_ImportBatches_ImportBatchId",
                table: "HoldingSnapshots");

            migrationBuilder.DropForeignKey(
                name: "FK_SoldEvents_ImportBatches_ImportBatchId",
                table: "SoldEvents");

            migrationBuilder.DropTable(
                name: "AnalysisRuns");

            migrationBuilder.DropTable(
                name: "ImportBatches");

            migrationBuilder.DropIndex(
                name: "IX_SoldEvents_ImportBatchId",
                table: "SoldEvents");

            migrationBuilder.DropIndex(
                name: "IX_NewsItems_SecurityId_PublishedAt",
                table: "NewsItems");

            migrationBuilder.DropIndex(
                name: "IX_NewsItems_Source_PublishedAt",
                table: "NewsItems");

            migrationBuilder.DropIndex(
                name: "IX_NewsItems_Url",
                table: "NewsItems");

            migrationBuilder.DropIndex(
                name: "IX_MarketPriceSnapshots_SecurityId_DataSource_FetchedAt",
                table: "MarketPriceSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_MarketPriceSnapshots_SecurityId_FetchedAt",
                table: "MarketPriceSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_HoldingSnapshots_ImportBatchId",
                table: "HoldingSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_HoldingSnapshots_SecurityId_SnapshotDate",
                table: "HoldingSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_DailyReports_AnalysisRunId",
                table: "DailyReports");

            migrationBuilder.DropIndex(
                name: "IX_AnalysisResults_AnalysisRunId",
                table: "AnalysisResults");

            migrationBuilder.DropColumn(
                name: "ImportBatchId",
                table: "SoldEvents");

            migrationBuilder.DropColumn(
                name: "ImportBatchId",
                table: "HoldingSnapshots");

            migrationBuilder.DropColumn(
                name: "AnalysisRunId",
                table: "DailyReports");

            migrationBuilder.DropColumn(
                name: "AnalysisRunId",
                table: "AnalysisResults");

            migrationBuilder.DropColumn(
                name: "InputDataSummaryJson",
                table: "AnalysisResults");

            migrationBuilder.DropColumn(
                name: "ScoreDetailsJson",
                table: "AnalysisResults");

            migrationBuilder.CreateIndex(
                name: "IX_NewsItems_SecurityId",
                table: "NewsItems",
                column: "SecurityId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketPriceSnapshots_SecurityId",
                table: "MarketPriceSnapshots",
                column: "SecurityId");

            migrationBuilder.CreateIndex(
                name: "IX_HoldingSnapshots_SecurityId",
                table: "HoldingSnapshots",
                column: "SecurityId");
        }
    }
}
