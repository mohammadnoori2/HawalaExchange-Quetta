using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HawalaExchange.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSaasAutomationAndNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SaasAutomationRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RunKey = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProcessedSubscriptions = table.Column<int>(type: "int", nullable: false),
                    CreatedInvoices = table.Column<int>(type: "int", nullable: false),
                    CreatedNotifications = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaasAutomationRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SaasAutomationSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AutoCreateInvoices = table.Column<bool>(type: "bit", nullable: false),
                    SendEmailNotifications = table.Column<bool>(type: "bit", nullable: false),
                    RunIntervalMinutes = table.Column<int>(type: "int", nullable: false),
                    ExpiryWarningDays = table.Column<int>(type: "int", nullable: false),
                    GracePeriodDays = table.Column<int>(type: "int", nullable: false),
                    GraceWarningDays = table.Column<int>(type: "int", nullable: false),
                    InvoiceLeadDays = table.Column<int>(type: "int", nullable: false),
                    InvoiceDueDays = table.Column<int>(type: "int", nullable: false),
                    QuotaWarningPercent = table.Column<int>(type: "int", nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaasAutomationSettings", x => x.Id);
                    table.CheckConstraint("CK_SaasAutomationSettings_Values", "[Id] = 1 AND [RunIntervalMinutes] >= 5 AND [ExpiryWarningDays] > 0 AND [GracePeriodDays] >= 0 AND [QuotaWarningPercent] BETWEEN 1 AND 100");
                });

            migrationBuilder.CreateTable(
                name: "SaasNotifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: true),
                    SubscriptionId = table.Column<long>(type: "bigint", nullable: true),
                    InvoiceId = table.Column<long>(type: "bigint", nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    DeduplicationKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    RecipientEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    DeliveryStatus = table.Column<int>(type: "int", nullable: false),
                    DeliveryError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReadAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaasNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaasNotifications_SubscriptionInvoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "SubscriptionInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaasNotifications_TenantSubscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "TenantSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaasNotifications_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SaasAutomationRuns_RunKey",
                table: "SaasAutomationRuns",
                column: "RunKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaasAutomationRuns_StartedAt_Status",
                table: "SaasAutomationRuns",
                columns: new[] { "StartedAt", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SaasNotifications_DeduplicationKey",
                table: "SaasNotifications",
                column: "DeduplicationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaasNotifications_InvoiceId",
                table: "SaasNotifications",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SaasNotifications_ReadAt_Severity_CreatedAt",
                table: "SaasNotifications",
                columns: new[] { "ReadAt", "Severity", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SaasNotifications_SubscriptionId",
                table: "SaasNotifications",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_SaasNotifications_TenantId_CreatedAt",
                table: "SaasNotifications",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.Sql("""
                SET IDENTITY_INSERT [SaasAutomationSettings] ON;
                INSERT INTO [SaasAutomationSettings]
                    ([Id],[IsEnabled],[AutoCreateInvoices],[SendEmailNotifications],[RunIntervalMinutes],[ExpiryWarningDays],[GracePeriodDays],[GraceWarningDays],[InvoiceLeadDays],[InvoiceDueDays],[QuotaWarningPercent])
                VALUES (1,1,1,0,360,30,7,2,7,7,80);
                SET IDENTITY_INSERT [SaasAutomationSettings] OFF;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SaasAutomationRuns");

            migrationBuilder.DropTable(
                name: "SaasAutomationSettings");

            migrationBuilder.DropTable(
                name: "SaasNotifications");
        }
    }
}
