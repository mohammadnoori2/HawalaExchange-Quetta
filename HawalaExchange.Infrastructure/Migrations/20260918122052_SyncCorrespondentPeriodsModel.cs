using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HawalaExchange.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncCorrespondentPeriodsModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Hawalas_TenantId_CorrespondentId_HawalaType_Number",
                table: "Hawalas");

            migrationBuilder.CreateTable(
                name: "CorrespondentAccountPeriods",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CorrespondentId = table.Column<long>(type: "bigint", nullable: false),
                    PeriodNumber = table.Column<int>(type: "int", nullable: false),
                    PeriodFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodTo = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ClosedBy = table.Column<long>(type: "bigint", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrespondentAccountPeriods", x => x.Id);
                    table.UniqueConstraint("AK_CorrespondentAccountPeriods_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_CorrespondentAccountPeriods_Correspondents_TenantId_CorrespondentId",
                        columns: x => new { x.TenantId, x.CorrespondentId },
                        principalTable: "Correspondents",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CorrespondentAccountPeriodBalances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    PeriodId = table.Column<long>(type: "bigint", nullable: false),
                    CurrencyId = table.Column<long>(type: "bigint", nullable: false),
                    TalabKar = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    BadehKar = table.Column<decimal>(type: "decimal(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrespondentAccountPeriodBalances", x => x.Id);
                    table.UniqueConstraint("AK_CorrespondentAccountPeriodBalances_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_CorrespondentAccountPeriodBalances_CorrespondentAccountPeriods_TenantId_PeriodId",
                        columns: x => new { x.TenantId, x.PeriodId },
                        principalTable: "CorrespondentAccountPeriods",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CorrespondentAccountPeriodBalances_Currencies_TenantId_CurrencyId",
                        columns: x => new { x.TenantId, x.CurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CorrespondentAccountPeriodHawalas",
                columns: table => new
                {
                    PeriodId = table.Column<long>(type: "bigint", nullable: false),
                    HawalaId = table.Column<long>(type: "bigint", nullable: false),
                    TenantId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrespondentAccountPeriodHawalas", x => new { x.PeriodId, x.HawalaId });
                    table.ForeignKey(
                        name: "FK_CorrespondentAccountPeriodHawalas_CorrespondentAccountPeriods_TenantId_PeriodId",
                        columns: x => new { x.TenantId, x.PeriodId },
                        principalTable: "CorrespondentAccountPeriods",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CorrespondentAccountPeriodHawalas_Hawalas_TenantId_HawalaId",
                        columns: x => new { x.TenantId, x.HawalaId },
                        principalTable: "Hawalas",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_TenantId_CorrespondentId_HawalaType_Number",
                table: "Hawalas",
                columns: new[] { "TenantId", "CorrespondentId", "HawalaType", "Number" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentAccountPeriodBalances_TenantId_CurrencyId",
                table: "CorrespondentAccountPeriodBalances",
                columns: new[] { "TenantId", "CurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentAccountPeriodBalances_TenantId_PeriodId_CurrencyId",
                table: "CorrespondentAccountPeriodBalances",
                columns: new[] { "TenantId", "PeriodId", "CurrencyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentAccountPeriodHawalas_TenantId_HawalaId",
                table: "CorrespondentAccountPeriodHawalas",
                columns: new[] { "TenantId", "HawalaId" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentAccountPeriodHawalas_TenantId_PeriodId",
                table: "CorrespondentAccountPeriodHawalas",
                columns: new[] { "TenantId", "PeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentAccountPeriods_TenantId_CorrespondentId_PeriodNumber",
                table: "CorrespondentAccountPeriods",
                columns: new[] { "TenantId", "CorrespondentId", "PeriodNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CorrespondentAccountPeriodBalances");

            migrationBuilder.DropTable(
                name: "CorrespondentAccountPeriodHawalas");

            migrationBuilder.DropTable(
                name: "CorrespondentAccountPeriods");

            migrationBuilder.DropIndex(
                name: "IX_Hawalas_TenantId_CorrespondentId_HawalaType_Number",
                table: "Hawalas");

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_TenantId_CorrespondentId_HawalaType_Number",
                table: "Hawalas",
                columns: new[] { "TenantId", "CorrespondentId", "HawalaType", "Number" },
                unique: true,
                filter: "[CorrespondentId] IS NOT NULL");
        }
    }
}
