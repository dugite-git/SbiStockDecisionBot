using System;
using InvestmentDecisionBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace InvestmentDecisionBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(BotDbContext))]
    [Migration("20260630090000_GenericExternalSymbols")]
    public partial class GenericExternalSymbols : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "AlphaVantageSymbol",
                table: "Securities",
                newName: "ExternalSymbol");

            migrationBuilder.RenameColumn(
                name: "AlphaVantageSymbolResolutionError",
                table: "Securities",
                newName: "ExternalSymbolResolutionError");

            migrationBuilder.RenameColumn(
                name: "AlphaVantageSymbolResolvedAt",
                table: "Securities",
                newName: "ExternalSymbolResolvedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ExternalSymbol",
                table: "Securities",
                newName: "AlphaVantageSymbol");

            migrationBuilder.RenameColumn(
                name: "ExternalSymbolResolutionError",
                table: "Securities",
                newName: "AlphaVantageSymbolResolutionError");

            migrationBuilder.RenameColumn(
                name: "ExternalSymbolResolvedAt",
                table: "Securities",
                newName: "AlphaVantageSymbolResolvedAt");
        }
    }
}
