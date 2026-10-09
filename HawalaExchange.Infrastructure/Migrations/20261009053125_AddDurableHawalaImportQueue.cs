using System;
using Microsoft.EntityFrameworkCore.Migrations;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

#nullable disable

namespace HawalaExchange.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableHawalaImportQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HawalaImportJobs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    BatchId = table.Column<long>(type: "bigint", nullable: false),
                    RequestedBy = table.Column<long>(type: "bigint", nullable: false),
                    RequestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProgressPercent = table.Column<int>(type: "int", nullable: false),
                    ProgressMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HawalaImportJobs", x => x.Id);
                    table.UniqueConstraint("AK_HawalaImportJobs_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_HawalaImportJobs_HawalaImportBatches_TenantId_BatchId",
                        columns: x => new { x.TenantId, x.BatchId },
                        principalTable: "HawalaImportBatches",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HawalaImportJobs_Status_CreatedAt",
                table: "HawalaImportJobs",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HawalaImportJobs_TenantId_BatchId",
                table: "HawalaImportJobs",
                columns: new[] { "TenantId", "BatchId" },
                unique: true);
            migrationBuilder.Sql(LoadProcedure("usp_CleanupHawalaImportStaging_v1").Replace(
                "AND [CreatedAt] < @CreatedBefore;",
                "AND [CreatedAt] < @CreatedBefore AND NOT EXISTS (SELECT 1 FROM dbo.HawalaImportJobs job WHERE job.TenantId = @TenantId AND job.BatchId = HawalaImportBatches.Id);"));
            migrationBuilder.Sql(LoadProcedure("usp_DeleteHawalaImportBatch_v1").Replace(
                "CREATE TABLE #HawalasToDelete",
                "IF EXISTS (SELECT 1 FROM dbo.HawalaImportJobs WHERE TenantId = @TenantId AND BatchId = @BatchId AND Status IN (N'Queued', N'Running')) THROW 51044, N'فایل در صف یا در حال ثبت است و قابل حذف نیست.', 1;\n        CREATE TABLE #HawalasToDelete"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(LoadProcedure("usp_CleanupHawalaImportStaging_v1"));
            migrationBuilder.Sql(LoadProcedure("usp_DeleteHawalaImportBatch_v1"));
            migrationBuilder.DropTable(
                name: "HawalaImportJobs");
        }

        private static string LoadProcedure(string name)
        {
            using var stream = typeof(AddDurableHawalaImportQueue).Assembly
                .GetManifestResourceStream("HawalaExchange.Infrastructure.Sql.ConsolidatedDatabaseObjects.sql")
                ?? throw new InvalidOperationException("Import SQL resource not found.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return Array.Find(Regex.Split(reader.ReadToEnd(), @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase),
                statement => statement.Contains($"PROCEDURE [dbo].[{name}]", StringComparison.OrdinalIgnoreCase))!.Trim();
        }
    }
}
