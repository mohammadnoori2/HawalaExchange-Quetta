using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HawalaExchange.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DecouplePaymentLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentLocations_Correspondents_TenantId_CorrespondentId",
                table: "PaymentLocations");

            migrationBuilder.DropIndex(
                name: "IX_PaymentLocations_TenantId_CorrespondentId",
                table: "PaymentLocations");

            migrationBuilder.DropColumn(
                name: "CorrespondentId",
                table: "PaymentLocations");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "PaymentLocations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                UPDATE [PaymentLocations]
                SET [NormalizedName] = LOWER(
                    REPLACE(
                        REPLACE(
                            REPLACE(
                                REPLACE(LTRIM(RTRIM([Name])), N'ي', N'ی'),
                            N'ك', N'ک'),
                        NCHAR(8204), N''),
                    NCHAR(8205), N''));

                WHILE EXISTS (
                    SELECT 1 FROM [PaymentLocations]
                    WHERE [NormalizedName] LIKE N'%  %')
                BEGIN
                    UPDATE [PaymentLocations]
                    SET [NormalizedName] = REPLACE([NormalizedName], N'  ', N' ')
                    WHERE [NormalizedName] LIKE N'%  %';
                END;

                ;WITH [RankedLocations] AS (
                    SELECT
                        [TenantId],
                        [Id],
                        MIN([Id]) OVER (
                            PARTITION BY [TenantId], [NormalizedName]
                        ) AS [CanonicalId]
                    FROM [PaymentLocations]
                )
                UPDATE [Hawalas]
                SET [PaymentLocationId] = [RankedLocations].[CanonicalId]
                FROM [Hawalas]
                INNER JOIN [RankedLocations]
                    ON [RankedLocations].[TenantId] = [Hawalas].[TenantId]
                    AND [RankedLocations].[Id] = [Hawalas].[PaymentLocationId]
                WHERE [RankedLocations].[Id] <> [RankedLocations].[CanonicalId];

                ;WITH [RankedLocations] AS (
                    SELECT
                        [TenantId],
                        [Id],
                        MIN([Id]) OVER (
                            PARTITION BY [TenantId], [NormalizedName]
                        ) AS [CanonicalId]
                    FROM [PaymentLocations]
                )
                DELETE [PaymentLocations]
                FROM [PaymentLocations]
                INNER JOIN [RankedLocations]
                    ON [RankedLocations].[TenantId] = [PaymentLocations].[TenantId]
                    AND [RankedLocations].[Id] = [PaymentLocations].[Id]
                WHERE [RankedLocations].[Id] <> [RankedLocations].[CanonicalId];
                """);

            migrationBuilder.CreateTable(
                name: "PaymentLocationAliases",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    PaymentLocationId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentLocationAliases", x => x.Id);
                    table.UniqueConstraint("AK_PaymentLocationAliases_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_PaymentLocationAliases_PaymentLocations_TenantId_PaymentLocationId",
                        columns: x => new { x.TenantId, x.PaymentLocationId },
                        principalTable: "PaymentLocations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PaymentLocationAliases_Users_TenantId_CreatedBy",
                        columns: x => new { x.TenantId, x.CreatedBy },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLocations_TenantId_NormalizedName",
                table: "PaymentLocations",
                columns: new[] { "TenantId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLocationAliases_TenantId_CreatedBy",
                table: "PaymentLocationAliases",
                columns: new[] { "TenantId", "CreatedBy" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLocationAliases_TenantId_NormalizedName",
                table: "PaymentLocationAliases",
                columns: new[] { "TenantId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLocationAliases_TenantId_PaymentLocationId",
                table: "PaymentLocationAliases",
                columns: new[] { "TenantId", "PaymentLocationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentLocationAliases");

            migrationBuilder.DropIndex(
                name: "IX_PaymentLocations_TenantId_NormalizedName",
                table: "PaymentLocations");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "PaymentLocations");

            migrationBuilder.AddColumn<long>(
                name: "CorrespondentId",
                table: "PaymentLocations",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLocations_TenantId_CorrespondentId",
                table: "PaymentLocations",
                columns: new[] { "TenantId", "CorrespondentId" });

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentLocations_Correspondents_TenantId_CorrespondentId",
                table: "PaymentLocations",
                columns: new[] { "TenantId", "CorrespondentId" },
                principalTable: "Correspondents",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
