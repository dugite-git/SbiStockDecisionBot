using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvestmentDecisionBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlphaVantageCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AlphaVantageSymbol",
                table: "Securities",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AlphaVantageSymbolResolutionError",
                table: "Securities",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AlphaVantageSymbolResolvedAt",
                table: "Securities",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExternalApiCacheEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Function = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CacheKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Succeeded = table.Column<bool>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalApiCacheEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExternalApiRequestLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Function = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CacheKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Succeeded = table.Column<bool>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalApiRequestLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalApiCacheEntries_ExpiresAt",
                table: "ExternalApiCacheEntries",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalApiCacheEntries_Provider_Function_CacheKey",
                table: "ExternalApiCacheEntries",
                columns: new[] { "Provider", "Function", "CacheKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalApiRequestLogs_Provider_RequestedAt",
                table: "ExternalApiRequestLogs",
                columns: new[] { "Provider", "RequestedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalApiCacheEntries");

            migrationBuilder.DropTable(
                name: "ExternalApiRequestLogs");

            migrationBuilder.DropColumn(
                name: "AlphaVantageSymbol",
                table: "Securities");

            migrationBuilder.DropColumn(
                name: "AlphaVantageSymbolResolutionError",
                table: "Securities");

            migrationBuilder.DropColumn(
                name: "AlphaVantageSymbolResolvedAt",
                table: "Securities");
        }
    }
}
