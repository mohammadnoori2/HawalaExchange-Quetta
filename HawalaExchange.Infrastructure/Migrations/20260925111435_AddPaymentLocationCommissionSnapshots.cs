using Microsoft.EntityFrameworkCore.Migrations;

using System.Text;
using System.Text.RegularExpressions;

#nullable disable

namespace HawalaExchange.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentLocationCommissionSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "PaymentLocationId",
                table: "CorrespondentCommissionBatchItems",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentLocationName",
                table: "CorrespondentCommissionBatchItems",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PerLakhRate",
                table: "CorrespondentCommissionBatchItems",
                type: "decimal(18,4)",
                nullable: true);
            migrationBuilder.Sql(LoadProcedure());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "This accounting migration cannot be downgraded automatically; restore a database backup instead.");
        }

        private static string LoadProcedure()
        {
            const string resourceName = "HawalaExchange.Infrastructure.Sql.ConsolidatedDatabaseObjects.sql";
            using var stream = typeof(AddPaymentLocationCommissionSnapshots).Assembly
                .GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded SQL resource '{resourceName}' was not found.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var procedure = Regex.Split(reader.ReadToEnd(), @"^\s*GO\s*$",
                    RegexOptions.Multiline | RegexOptions.IgnoreCase)
                .Single(x => x.Contains("CREATE PROCEDURE [dbo].[usp_ProcessPeriodicCommission_v1]",
                    StringComparison.OrdinalIgnoreCase));
            return Regex.Replace(procedure.Trim(), @"^\s*CREATE\s+PROCEDURE",
                "CREATE OR ALTER PROCEDURE", RegexOptions.IgnoreCase);
        }
    }
}
