using System;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Text;
using System.Text.RegularExpressions;

#nullable disable

namespace HawalaExchange.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyCommissionValuation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'dbo.Hawalas', N'CommissionBaseUsdAmount') IS NULL
                    ALTER TABLE [dbo].[Hawalas] ADD [CommissionBaseUsdAmount] decimal(18,8) NULL;
                IF COL_LENGTH(N'dbo.Hawalas', N'CommissionUsdToAfnRate') IS NULL
                    ALTER TABLE [dbo].[Hawalas] ADD [CommissionUsdToAfnRate] decimal(18,8) NULL;
                IF COL_LENGTH(N'dbo.Hawalas', N'CommissionValuationDate') IS NULL
                    ALTER TABLE [dbo].[Hawalas] ADD [CommissionValuationDate] datetime2 NULL;
                IF COL_LENGTH(N'dbo.Hawalas', N'CommissionValuedAt') IS NULL
                    ALTER TABLE [dbo].[Hawalas] ADD [CommissionValuedAt] datetime2 NULL;
                """);

            migrationBuilder.CreateTable(
                name: "DailyCommissionRates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    RateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsdToAfnRate = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedBy = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyCommissionRates", x => x.Id);
                    table.UniqueConstraint("AK_DailyCommissionRates_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyCommissionRates_TenantId_RateDate",
                table: "DailyCommissionRates",
                columns: new[] { "TenantId", "RateDate" },
                unique: true);

            migrationBuilder.Sql(LoadPeriodicCommissionProcedure());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyCommissionRates");

            migrationBuilder.DropColumn(
                name: "CommissionBaseUsdAmount",
                table: "Hawalas");

            migrationBuilder.DropColumn(
                name: "CommissionUsdToAfnRate",
                table: "Hawalas");

            migrationBuilder.DropColumn(
                name: "CommissionValuationDate",
                table: "Hawalas");

            migrationBuilder.DropColumn(
                name: "CommissionValuedAt",
                table: "Hawalas");
        }

        private static string LoadPeriodicCommissionProcedure()
        {
            const string resourceName =
                "HawalaExchange.Infrastructure.Sql.ConsolidatedDatabaseObjects.sql";
            using var stream = typeof(AddDailyCommissionValuation).Assembly
                .GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded SQL resource '{resourceName}' was not found.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var procedure = Regex.Split(
                    reader.ReadToEnd(), @"^\s*GO\s*$",
                    RegexOptions.Multiline | RegexOptions.IgnoreCase)
                .Single(x => x.Contains(
                    "CREATE PROCEDURE [dbo].[usp_ProcessPeriodicCommission_v1]",
                    StringComparison.OrdinalIgnoreCase));
            return Regex.Replace(
                procedure.Trim(), @"^\s*CREATE\s+PROCEDURE",
                "CREATE OR ALTER PROCEDURE", RegexOptions.IgnoreCase);
        }
    }
}
