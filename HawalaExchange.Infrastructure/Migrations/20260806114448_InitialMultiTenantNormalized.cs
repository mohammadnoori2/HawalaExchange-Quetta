using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HawalaExchange.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialMultiTenantNormalized : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillingNumberSequences",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Prefix = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    NextValue = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingNumberSequences", x => x.Id);
                    table.CheckConstraint("CK_BillingNumberSequences_NextValue", "[NextValue] > 0");
                });

            migrationBuilder.CreateTable(
                name: "Currencies",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DecimalPlaces = table.Column<int>(type: "int", nullable: false, defaultValue: 2),
                    QuotationPriority = table.Column<int>(type: "int", nullable: false, defaultValue: 1000),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Currencies", x => x.Id);
                    table.UniqueConstraint("AK_Currencies_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CustomerCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FatherName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TazkiraNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PhotoPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TazkiraImagePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    Remarks = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                    table.UniqueConstraint("AK_Customers_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

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
                name: "SubscriptionPlans",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    MonthlyPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AnnualPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TrialDays = table.Column<int>(type: "int", nullable: false),
                    MaxUsers = table.Column<int>(type: "int", nullable: false),
                    MaxBranches = table.Column<int>(type: "int", nullable: false),
                    MaxStorageBytes = table.Column<long>(type: "bigint", nullable: false),
                    MaxMonthlyTransactions = table.Column<int>(type: "int", nullable: false),
                    IncludesAdvancedReports = table.Column<bool>(type: "bit", nullable: false),
                    IncludesDocumentManagement = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlans", x => x.Id);
                    table.CheckConstraint("CK_SubscriptionPlans_Limits", "[TrialDays] >= 0 AND [MaxUsers] >= 0 AND [MaxBranches] >= 0 AND [MaxStorageBytes] >= 0 AND [MaxMonthlyTransactions] >= 0");
                    table.CheckConstraint("CK_SubscriptionPlans_Prices", "[MonthlyPrice] >= 0 AND [AnnualPrice] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LegalName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ContactName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ContactEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ContactPhone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Correspondents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    SettlementCurrencyId = table.Column<long>(type: "bigint", nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Correspondents", x => x.Id);
                    table.UniqueConstraint("AK_Correspondents_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Correspondents_Currencies_TenantId_SettlementCurrencyId",
                        columns: x => new { x.TenantId, x.SettlementCurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<long>(type: "bigint", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoleClaims_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Branches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branches", x => x.Id);
                    table.UniqueConstraint("AK_Branches_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Branches_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompanySettings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LogoPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    WhatsAppNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TelegramUserName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FooterNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DefaultProfitCurrencyId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanySettings", x => x.Id);
                    table.UniqueConstraint("AK_CompanySettings_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_CompanySettings_Currencies_TenantId_DefaultProfitCurrencyId",
                        columns: x => new { x.TenantId, x.DefaultProfitCurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanySettings_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlatformAuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActorUserId = table.Column<long>(type: "bigint", nullable: true),
                    TenantId = table.Column<long>(type: "bigint", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    EntityName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<long>(type: "bigint", nullable: true),
                    Details = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformAuditLogs_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TenantSubscriptions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    PlanId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    BillingCycle = table.Column<int>(type: "int", nullable: false),
                    StartAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TrialEndAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GracePeriodEndAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AutoRenew = table.Column<bool>(type: "bit", nullable: false),
                    AgreedPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    LastPaymentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NextPaymentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AdministrativeNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SuspensionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSubscriptions", x => x.Id);
                    table.CheckConstraint("CK_TenantSubscriptions_Dates", "[EndAt] > [StartAt]");
                    table.CheckConstraint("CK_TenantSubscriptions_Price", "[AgreedPrice] >= 0");
                    table.ForeignKey(
                        name: "FK_TenantSubscriptions_SubscriptionPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "SubscriptionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TenantSubscriptions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TenantUsageSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "date", nullable: false),
                    UserCount = table.Column<int>(type: "int", nullable: false),
                    BranchCount = table.Column<int>(type: "int", nullable: false),
                    TransactionCount = table.Column<int>(type: "int", nullable: false),
                    StorageBytes = table.Column<long>(type: "bigint", nullable: false),
                    CalculatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantUsageSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantUsageSnapshots_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    AccountCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AccountName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AccountType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CustomerId = table.Column<long>(type: "bigint", nullable: true),
                    CorrespondentId = table.Column<long>(type: "bigint", nullable: true),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                    table.UniqueConstraint("AK_Accounts_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Accounts_OneOwner", "[CustomerId] IS NULL OR [CorrespondentId] IS NULL");
                    table.ForeignKey(
                        name: "FK_Accounts_Correspondents_TenantId_CorrespondentId",
                        columns: x => new { x.TenantId, x.CorrespondentId },
                        principalTable: "Correspondents",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Accounts_Customers_TenantId_CustomerId",
                        columns: x => new { x.TenantId, x.CustomerId },
                        principalTable: "Customers",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    LocalUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BranchId = table.Column<long>(type: "bigint", nullable: true),
                    IsPlatformUser = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    LastLoginAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RefreshToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RefreshTokenExpiryTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.UniqueConstraint("AK_Users_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Users_Scope", "([IsPlatformUser] = 1 AND [BranchId] IS NULL) OR ([IsPlatformUser] = 0 AND [BranchId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Users_Branches_TenantId_BranchId",
                        columns: x => new { x.TenantId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Users_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionInvoices",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    SubscriptionId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DueAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ServicePeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ServicePeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaidAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    BillingName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BillingEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    BillingPhone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AutoRenewOnPayment = table.Column<bool>(type: "bit", nullable: false),
                    RenewalAppliedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionInvoices", x => x.Id);
                    table.CheckConstraint("CK_SubscriptionInvoices_Amounts", "[Subtotal] >= 0 AND [DiscountAmount] >= 0 AND [TaxAmount] >= 0 AND [TotalAmount] >= 0 AND [PaidAmount] >= 0 AND [PaidAmount] <= [TotalAmount]");
                    table.CheckConstraint("CK_SubscriptionInvoices_Dates", "[ServicePeriodEnd] > [ServicePeriodStart] AND [DueAt] >= [IssuedAt]");
                    table.ForeignKey(
                        name: "FK_SubscriptionInvoices_TenantSubscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "TenantSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionInvoices_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccountMoneyOperations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    OperationType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OperationDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    CashOrBankAccountId = table.Column<long>(type: "bigint", nullable: false),
                    CurrencyId = table.Column<long>(type: "bigint", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountMoneyOperations", x => x.Id);
                    table.UniqueConstraint("AK_AccountMoneyOperations_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_AccountMoneyOperations_Accounts_TenantId_AccountId",
                        columns: x => new { x.TenantId, x.AccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountMoneyOperations_Accounts_TenantId_CashOrBankAccountId",
                        columns: x => new { x.TenantId, x.CashOrBankAccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountMoneyOperations_Currencies_TenantId_CurrencyId",
                        columns: x => new { x.TenantId, x.CurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CapitalInvestments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CurrencyId = table.Column<long>(type: "bigint", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ProfitCurrencyId = table.Column<long>(type: "bigint", nullable: true),
                    ProfitCurrencyAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    ReceivingAccountId = table.Column<long>(type: "bigint", nullable: false),
                    CapitalAccountId = table.Column<long>(type: "bigint", nullable: false),
                    InvestmentDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapitalInvestments", x => x.Id);
                    table.UniqueConstraint("AK_CapitalInvestments_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_CapitalInvestments_Accounts_TenantId_CapitalAccountId",
                        columns: x => new { x.TenantId, x.CapitalAccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CapitalInvestments_Accounts_TenantId_ReceivingAccountId",
                        columns: x => new { x.TenantId, x.ReceivingAccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CapitalInvestments_Currencies_TenantId_CurrencyId",
                        columns: x => new { x.TenantId, x.CurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CapitalInvestments_Currencies_TenantId_ProfitCurrencyId",
                        columns: x => new { x.TenantId, x.ProfitCurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashDailyBalances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    JournalDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    CurrencyId = table.Column<long>(type: "bigint", nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ClosingBalance = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    IsClosed = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashDailyBalances", x => x.Id);
                    table.UniqueConstraint("AK_CashDailyBalances_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_CashDailyBalances_Accounts_TenantId_AccountId",
                        columns: x => new { x.TenantId, x.AccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashDailyBalances_Currencies_TenantId_CurrencyId",
                        columns: x => new { x.TenantId, x.CurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MoneyExchangeOperations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    ExchangeDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FromAccountId = table.Column<long>(type: "bigint", nullable: false),
                    FromCurrencyId = table.Column<long>(type: "bigint", nullable: false),
                    FromAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ToAccountId = table.Column<long>(type: "bigint", nullable: false),
                    ToCurrencyId = table.Column<long>(type: "bigint", nullable: false),
                    ToAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    RateBaseCurrencyId = table.Column<long>(type: "bigint", nullable: true),
                    RateQuoteCurrencyId = table.Column<long>(type: "bigint", nullable: true),
                    OperationType = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "Treasury"),
                    ProfitCurrencyId = table.Column<long>(type: "bigint", nullable: true),
                    CommissionAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ExternalFeeAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CostAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RealizedProfit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ExchangeProfitAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    InventoryCostIncrease = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    InventoryCostDecrease = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ShortLiabilityIncrease = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ShortLiabilityDecrease = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DeferredAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ProfitStatus = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "NotCalculated"),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoneyExchangeOperations", x => x.Id);
                    table.UniqueConstraint("AK_MoneyExchangeOperations_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_MoneyExchangeOperations_Accounts_TenantId_FromAccountId",
                        columns: x => new { x.TenantId, x.FromAccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MoneyExchangeOperations_Accounts_TenantId_ToAccountId",
                        columns: x => new { x.TenantId, x.ToAccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MoneyExchangeOperations_Currencies_TenantId_FromCurrencyId",
                        columns: x => new { x.TenantId, x.FromCurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MoneyExchangeOperations_Currencies_TenantId_ProfitCurrencyId",
                        columns: x => new { x.TenantId, x.ProfitCurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MoneyExchangeOperations_Currencies_TenantId_RateBaseCurrencyId",
                        columns: x => new { x.TenantId, x.RateBaseCurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MoneyExchangeOperations_Currencies_TenantId_RateQuoteCurrencyId",
                        columns: x => new { x.TenantId, x.RateQuoteCurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MoneyExchangeOperations_Currencies_TenantId_ToCurrencyId",
                        columns: x => new { x.TenantId, x.ToCurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccountBadehkarLimits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    CurrencyId = table.Column<long>(type: "bigint", nullable: false),
                    BadehkarLimit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountBadehkarLimits", x => x.Id);
                    table.UniqueConstraint("AK_AccountBadehkarLimits_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_AccountBadehkarLimits_Accounts_TenantId_AccountId",
                        columns: x => new { x.TenantId, x.AccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountBadehkarLimits_Currencies_TenantId_CurrencyId",
                        columns: x => new { x.TenantId, x.CurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountBadehkarLimits_Users_TenantId_CreatedBy",
                        columns: x => new { x.TenantId, x.CreatedBy },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashBalanceAlertSettings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    CurrencyId = table.Column<long>(type: "bigint", nullable: false),
                    MinimumBalance = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    NotifyAllUsers = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    ShowInApp = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashBalanceAlertSettings", x => x.Id);
                    table.UniqueConstraint("AK_CashBalanceAlertSettings_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_CashBalanceAlertSettings_Accounts_TenantId_AccountId",
                        columns: x => new { x.TenantId, x.AccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashBalanceAlertSettings_Currencies_TenantId_CurrencyId",
                        columns: x => new { x.TenantId, x.CurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashBalanceAlertSettings_Users_TenantId_CreatedBy",
                        columns: x => new { x.TenantId, x.CreatedBy },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExchangeRates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    FromCurrencyId = table.Column<long>(type: "bigint", nullable: false),
                    ToCurrencyId = table.Column<long>(type: "bigint", nullable: false),
                    BuyRate = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    SellRate = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRates", x => x.Id);
                    table.UniqueConstraint("AK_ExchangeRates_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ExchangeRates_Currencies_TenantId_FromCurrencyId",
                        columns: x => new { x.TenantId, x.FromCurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExchangeRates_Currencies_TenantId_ToCurrencyId",
                        columns: x => new { x.TenantId, x.ToCurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExchangeRates_Users_TenantId_CreatedBy",
                        columns: x => new { x.TenantId, x.CreatedBy },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentLocations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CorrespondentId = table.Column<long>(type: "bigint", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ContactPerson = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentLocations", x => x.Id);
                    table.UniqueConstraint("AK_PaymentLocations_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_PaymentLocations_Correspondents_TenantId_CorrespondentId",
                        columns: x => new { x.TenantId, x.CorrespondentId },
                        principalTable: "Correspondents",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentLocations_Users_TenantId_CreatedBy",
                        columns: x => new { x.TenantId, x.CreatedBy },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PaymentLocations_Users_TenantId_UpdatedBy",
                        columns: x => new { x.TenantId, x.UpdatedBy },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "UserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserClaims_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_UserLogins_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    RoleId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserTokens",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_UserTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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

            migrationBuilder.CreateTable(
                name: "SubscriptionInvoiceItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceId = table.Column<long>(type: "bigint", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionInvoiceItems", x => x.Id);
                    table.CheckConstraint("CK_SubscriptionInvoiceItems_Amounts", "[Quantity] > 0 AND [UnitPrice] >= 0 AND [DiscountAmount] >= 0 AND [TaxAmount] >= 0 AND [LineTotal] >= 0");
                    table.ForeignKey(
                        name: "FK_SubscriptionInvoiceItems_SubscriptionInvoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "SubscriptionInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionPayments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubscriptionId = table.Column<long>(type: "bigint", nullable: false),
                    InvoiceId = table.Column<long>(type: "bigint", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DueAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PaymentMethod = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ProviderName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProviderTransactionId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReceiptNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPayments", x => x.Id);
                    table.CheckConstraint("CK_SubscriptionPayments_Amount", "[Amount] >= 0");
                    table.ForeignKey(
                        name: "FK_SubscriptionPayments_SubscriptionInvoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "SubscriptionInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionPayments_TenantSubscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "TenantSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashBalanceAlertRecipients",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    SettingId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashBalanceAlertRecipients", x => x.Id);
                    table.UniqueConstraint("AK_CashBalanceAlertRecipients_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_CashBalanceAlertRecipients_CashBalanceAlertSettings_TenantId_SettingId",
                        columns: x => new { x.TenantId, x.SettingId },
                        principalTable: "CashBalanceAlertSettings",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CashBalanceAlertRecipients_Users_TenantId_UserId",
                        columns: x => new { x.TenantId, x.UserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashBalanceAlerts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    SettingId = table.Column<long>(type: "bigint", nullable: false),
                    CurrentBalance = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    MinimumBalance = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    TriggeredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastCheckedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashBalanceAlerts", x => x.Id);
                    table.UniqueConstraint("AK_CashBalanceAlerts_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_CashBalanceAlerts_CashBalanceAlertSettings_TenantId_SettingId",
                        columns: x => new { x.TenantId, x.SettingId },
                        principalTable: "CashBalanceAlertSettings",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Hawalas",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false),
                    HawalaType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CorrespondentId = table.Column<long>(type: "bigint", nullable: true),
                    PaymentLocationId = table.Column<long>(type: "bigint", maxLength: 200, nullable: true),
                    SenderName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SenderFatherName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SenderTazkiraImagePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SenderPhone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SenderTazkiraNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ReceiverName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReceiverFatherName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReceiverTazkiraImagePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReceiverPhone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ReceiverTazkiraNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FromCurrencyId = table.Column<long>(type: "bigint", nullable: false),
                    FromAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 4, nullable: false),
                    ToCurrencyId = table.Column<long>(type: "bigint", nullable: false),
                    ToAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 4, nullable: true),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 8, nullable: true),
                    CommissionAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 4, nullable: true),
                    CommissionCurrencyId = table.Column<long>(type: "bigint", nullable: true),
                    AgentCommissionAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 4, nullable: true),
                    AgentCommissionCurrencyId = table.Column<long>(type: "bigint", nullable: true),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SenderAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReceiverAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Pending"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PaidBy = table.Column<long>(type: "bigint", nullable: true),
                    PaidFromAccountId = table.Column<long>(type: "bigint", nullable: true),
                    SourceHawalaId = table.Column<long>(type: "bigint", nullable: true),
                    IsSystemGenerated = table.Column<bool>(type: "bit", nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledBy = table.Column<long>(type: "bigint", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReversedTransactionId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hawalas", x => x.Id);
                    table.UniqueConstraint("AK_Hawalas_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Hawala_CommissionAmount_NonNegative_IfNotNull", "[CommissionAmount] IS NULL OR [CommissionAmount] >= 0");
                    table.CheckConstraint("CK_Hawala_ExchangeRate_Positive_IfNotNull", "[ExchangeRate] IS NULL OR [ExchangeRate] > 0");
                    table.CheckConstraint("CK_Hawala_FromAmount_Positive", "[FromAmount] > 0");
                    table.CheckConstraint("CK_Hawala_HawalaType_Valid", "[HawalaType] IN ('HawalaSend', 'HawalaReceive', 'HawalaOther')");
                    table.CheckConstraint("CK_Hawala_Status_Valid", "[Status] IN ('Pending', 'Paid', 'Cancel')");
                    table.CheckConstraint("CK_Hawala_ToAmount_Positive_IfNotNull", "[ToAmount] IS NULL OR [ToAmount] > 0");
                    table.ForeignKey(
                        name: "FK_Hawalas_Accounts_TenantId_PaidFromAccountId",
                        columns: x => new { x.TenantId, x.PaidFromAccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Hawalas_Correspondents_TenantId_CorrespondentId",
                        columns: x => new { x.TenantId, x.CorrespondentId },
                        principalTable: "Correspondents",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Hawalas_Currencies_TenantId_AgentCommissionCurrencyId",
                        columns: x => new { x.TenantId, x.AgentCommissionCurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Hawalas_Currencies_TenantId_CommissionCurrencyId",
                        columns: x => new { x.TenantId, x.CommissionCurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Hawalas_Currencies_TenantId_FromCurrencyId",
                        columns: x => new { x.TenantId, x.FromCurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Hawalas_Currencies_TenantId_ToCurrencyId",
                        columns: x => new { x.TenantId, x.ToCurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Hawalas_Hawalas_TenantId_SourceHawalaId",
                        columns: x => new { x.TenantId, x.SourceHawalaId },
                        principalTable: "Hawalas",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Hawalas_PaymentLocations_TenantId_PaymentLocationId",
                        columns: x => new { x.TenantId, x.PaymentLocationId },
                        principalTable: "PaymentLocations",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_Hawalas_Users_TenantId_CancelledBy",
                        columns: x => new { x.TenantId, x.CancelledBy },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Hawalas_Users_TenantId_CreatedBy",
                        columns: x => new { x.TenantId, x.CreatedBy },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Hawalas_Users_TenantId_PaidBy",
                        columns: x => new { x.TenantId, x.PaidBy },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    TransactionNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TransactionType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    BranchId = table.Column<long>(type: "bigint", nullable: false),
                    CustomerId = table.Column<long>(type: "bigint", nullable: true),
                    CustomerFullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CancelledBy = table.Column<long>(type: "bigint", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReversedTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    HawalaId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                    table.UniqueConstraint("AK_Transactions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Transactions_Branches_TenantId_BranchId",
                        columns: x => new { x.TenantId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transactions_Customers_TenantId_CustomerId",
                        columns: x => new { x.TenantId, x.CustomerId },
                        principalTable: "Customers",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transactions_Hawalas_TenantId_HawalaId",
                        columns: x => new { x.TenantId, x.HawalaId },
                        principalTable: "Hawalas",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_Transactions_Transactions_TenantId_ReversedTransactionId",
                        columns: x => new { x.TenantId, x.ReversedTransactionId },
                        principalTable: "Transactions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transactions_Users_TenantId_CancelledBy",
                        columns: x => new { x.TenantId, x.CancelledBy },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transactions_Users_TenantId_CreatedBy",
                        columns: x => new { x.TenantId, x.CreatedBy },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: true),
                    ProcessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TableName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RecordId = table.Column<long>(type: "bigint", nullable: false),
                    OldValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TransactionId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.UniqueConstraint("AK_AuditLogs_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_AuditLogs_Transactions_TenantId_TransactionId",
                        columns: x => new { x.TenantId, x.TransactionId },
                        principalTable: "Transactions",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_AuditLogs_Users_TenantId_UserId",
                        columns: x => new { x.TenantId, x.UserId },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CorrespondentSettlementConversions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    CorrespondentId = table.Column<long>(type: "bigint", nullable: false),
                    TargetCurrencyId = table.Column<long>(type: "bigint", nullable: false),
                    TransactionId = table.Column<long>(type: "bigint", nullable: false),
                    SourceMode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrespondentSettlementConversions", x => x.Id);
                    table.UniqueConstraint("AK_CorrespondentSettlementConversions_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_CorrespondentSettlementConversions_Correspondents_TenantId_CorrespondentId",
                        columns: x => new { x.TenantId, x.CorrespondentId },
                        principalTable: "Correspondents",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CorrespondentSettlementConversions_Currencies_TenantId_TargetCurrencyId",
                        columns: x => new { x.TenantId, x.TargetCurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CorrespondentSettlementConversions_Transactions_TenantId_TransactionId",
                        columns: x => new { x.TenantId, x.TransactionId },
                        principalTable: "Transactions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CorrespondentSettlementConversions_Users_TenantId_CreatedBy",
                        columns: x => new { x.TenantId, x.CreatedBy },
                        principalTable: "Users",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    TransactionId = table.Column<long>(type: "bigint", nullable: true),
                    CustomerId = table.Column<long>(type: "bigint", nullable: true),
                    CorrespondentId = table.Column<long>(type: "bigint", nullable: true),
                    AccountId = table.Column<long>(type: "bigint", nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                    table.UniqueConstraint("AK_Documents_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Documents_ExactlyOneOwner", "(CASE WHEN [TransactionId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [CustomerId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [CorrespondentId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [AccountId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_Documents_Accounts_TenantId_AccountId",
                        columns: x => new { x.TenantId, x.AccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Documents_Correspondents_TenantId_CorrespondentId",
                        columns: x => new { x.TenantId, x.CorrespondentId },
                        principalTable: "Correspondents",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Documents_Customers_TenantId_CustomerId",
                        columns: x => new { x.TenantId, x.CustomerId },
                        principalTable: "Customers",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Documents_Transactions_TenantId_TransactionId",
                        columns: x => new { x.TenantId, x.TransactionId },
                        principalTable: "Transactions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    ExpenseDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CurrencyId = table.Column<long>(type: "bigint", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ExpenseAccountId = table.Column<long>(type: "bigint", nullable: false),
                    PaidFromAccountId = table.Column<long>(type: "bigint", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<long>(type: "bigint", nullable: true),
                    TransactionId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                    table.UniqueConstraint("AK_Expenses_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Expenses_Accounts_TenantId_ExpenseAccountId",
                        columns: x => new { x.TenantId, x.ExpenseAccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_Accounts_TenantId_PaidFromAccountId",
                        columns: x => new { x.TenantId, x.PaidFromAccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_Currencies_TenantId_CurrencyId",
                        columns: x => new { x.TenantId, x.CurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_Transactions_TenantId_TransactionId",
                        columns: x => new { x.TenantId, x.TransactionId },
                        principalTable: "Transactions",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "TransactionDetails",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    TransactionId = table.Column<long>(type: "bigint", nullable: false),
                    FromCurrencyId = table.Column<long>(type: "bigint", nullable: true),
                    FromAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    ToCurrencyId = table.Column<long>(type: "bigint", nullable: true),
                    ToAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    TransferAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    CommissionCurrencyId = table.Column<long>(type: "bigint", nullable: true),
                    CommissionAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    AgentCommissionCurrencyId = table.Column<long>(type: "bigint", nullable: true),
                    AgentCommissionAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CorrespondentId = table.Column<long>(type: "bigint", nullable: true),
                    SenderName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SenderPhone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SenderTazkiraNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReceiverName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReceiverPhone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ReceiverTazkiraNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionDetails", x => x.Id);
                    table.UniqueConstraint("AK_TransactionDetails_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_TransactionDetails_Correspondents_TenantId_CorrespondentId",
                        columns: x => new { x.TenantId, x.CorrespondentId },
                        principalTable: "Correspondents",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransactionDetails_Currencies_TenantId_AgentCommissionCurrencyId",
                        columns: x => new { x.TenantId, x.AgentCommissionCurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransactionDetails_Currencies_TenantId_CommissionCurrencyId",
                        columns: x => new { x.TenantId, x.CommissionCurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransactionDetails_Currencies_TenantId_FromCurrencyId",
                        columns: x => new { x.TenantId, x.FromCurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransactionDetails_Currencies_TenantId_ToCurrencyId",
                        columns: x => new { x.TenantId, x.ToCurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransactionDetails_Transactions_TenantId_TransactionId",
                        columns: x => new { x.TenantId, x.TransactionId },
                        principalTable: "Transactions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Transfers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    FromAccountId = table.Column<long>(type: "bigint", nullable: false),
                    ToAccountId = table.Column<long>(type: "bigint", nullable: false),
                    CurrencyId = table.Column<long>(type: "bigint", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ProfitCurrencyId = table.Column<long>(type: "bigint", nullable: true),
                    ProfitCurrencyAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    TransferMethod = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TransactionId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transfers", x => x.Id);
                    table.UniqueConstraint("AK_Transfers_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Transfers_Accounts_TenantId_FromAccountId",
                        columns: x => new { x.TenantId, x.FromAccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transfers_Accounts_TenantId_ToAccountId",
                        columns: x => new { x.TenantId, x.ToAccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transfers_Currencies_TenantId_CurrencyId",
                        columns: x => new { x.TenantId, x.CurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transfers_Currencies_TenantId_ProfitCurrencyId",
                        columns: x => new { x.TenantId, x.ProfitCurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transfers_Transactions_TenantId_TransactionId",
                        columns: x => new { x.TenantId, x.TransactionId },
                        principalTable: "Transactions",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "CorrespondentSettlementConversionHawalaItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    ConversionId = table.Column<long>(type: "bigint", nullable: false),
                    HawalaId = table.Column<long>(type: "bigint", nullable: false),
                    SourceCurrencyId = table.Column<long>(type: "bigint", nullable: false),
                    SourceTalabKar = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    SourceBadehKar = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    TargetTalabKar = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TargetBadehKar = table.Column<decimal>(type: "decimal(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrespondentSettlementConversionHawalaItems", x => x.Id);
                    table.UniqueConstraint("AK_CorrespondentSettlementConversionHawalaItems_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_CorrespondentSettlementConversionHawalaItems_CorrespondentSettlementConversions_TenantId_ConversionId",
                        columns: x => new { x.TenantId, x.ConversionId },
                        principalTable: "CorrespondentSettlementConversions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CorrespondentSettlementConversionHawalaItems_Currencies_TenantId_SourceCurrencyId",
                        columns: x => new { x.TenantId, x.SourceCurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CorrespondentSettlementConversionHawalaItems_Hawalas_TenantId_HawalaId",
                        columns: x => new { x.TenantId, x.HawalaId },
                        principalTable: "Hawalas",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CorrespondentSettlementConversionHawalas",
                columns: table => new
                {
                    ConversionId = table.Column<long>(type: "bigint", nullable: false),
                    HawalaId = table.Column<long>(type: "bigint", nullable: false),
                    TenantId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrespondentSettlementConversionHawalas", x => new { x.ConversionId, x.HawalaId });
                    table.ForeignKey(
                        name: "FK_CorrespondentSettlementConversionHawalas_CorrespondentSettlementConversions_TenantId_ConversionId",
                        columns: x => new { x.TenantId, x.ConversionId },
                        principalTable: "CorrespondentSettlementConversions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CorrespondentSettlementConversionHawalas_Hawalas_TenantId_HawalaId",
                        columns: x => new { x.TenantId, x.HawalaId },
                        principalTable: "Hawalas",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CorrespondentSettlementConversionItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    ConversionId = table.Column<long>(type: "bigint", nullable: false),
                    SourceCurrencyId = table.Column<long>(type: "bigint", nullable: false),
                    SourceTalabKar = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    SourceBadehKar = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    TargetTalabKar = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TargetBadehKar = table.Column<decimal>(type: "decimal(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrespondentSettlementConversionItems", x => x.Id);
                    table.UniqueConstraint("AK_CorrespondentSettlementConversionItems_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_CorrespondentSettlementConversionItems_CorrespondentSettlementConversions_TenantId_ConversionId",
                        columns: x => new { x.TenantId, x.ConversionId },
                        principalTable: "CorrespondentSettlementConversions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CorrespondentSettlementConversionItems_Currencies_TenantId_SourceCurrencyId",
                        columns: x => new { x.TenantId, x.SourceCurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LedgerEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    TransferId = table.Column<long>(type: "bigint", nullable: true),
                    HawalaId = table.Column<long>(type: "bigint", nullable: true),
                    SettlementHawalaItemId = table.Column<long>(type: "bigint", nullable: true),
                    TransactionId = table.Column<long>(type: "bigint", nullable: true),
                    CapitalInvestmentId = table.Column<long>(type: "bigint", nullable: true),
                    ExpenseId = table.Column<long>(type: "bigint", nullable: true),
                    AccountMoneyOperationId = table.Column<long>(type: "bigint", nullable: true),
                    MoneyExchangeOperationId = table.Column<long>(type: "bigint", nullable: true),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    CurrencyId = table.Column<long>(type: "bigint", nullable: false),
                    TalabKar = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BadehKar = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerEntries", x => x.Id);
                    table.UniqueConstraint("AK_LedgerEntries_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_LedgerEntries_AtMostOneSource", "(CASE WHEN [TransactionId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [HawalaId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [TransferId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [CapitalInvestmentId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [ExpenseId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [AccountMoneyOperationId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [MoneyExchangeOperationId] IS NULL THEN 0 ELSE 1 END) <= 1");
                    table.CheckConstraint("CK_LedgerEntries_BadehKar_NonNegative", "[BadehKar] >= 0");
                    table.CheckConstraint("CK_LedgerEntries_OnlyOneSide", "([TalabKar] > 0 AND [BadehKar] = 0) OR ([TalabKar] = 0 AND [BadehKar] > 0)");
                    table.CheckConstraint("CK_LedgerEntries_TalabKar_NonNegative", "[TalabKar] >= 0");
                    table.ForeignKey(
                        name: "FK_LedgerEntries_AccountMoneyOperations_TenantId_AccountMoneyOperationId",
                        columns: x => new { x.TenantId, x.AccountMoneyOperationId },
                        principalTable: "AccountMoneyOperations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Accounts_TenantId_AccountId",
                        columns: x => new { x.TenantId, x.AccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_CapitalInvestments_TenantId_CapitalInvestmentId",
                        columns: x => new { x.TenantId, x.CapitalInvestmentId },
                        principalTable: "CapitalInvestments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_CorrespondentSettlementConversionHawalaItems_TenantId_SettlementHawalaItemId",
                        columns: x => new { x.TenantId, x.SettlementHawalaItemId },
                        principalTable: "CorrespondentSettlementConversionHawalaItems",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Currencies_TenantId_CurrencyId",
                        columns: x => new { x.TenantId, x.CurrencyId },
                        principalTable: "Currencies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Expenses_TenantId_ExpenseId",
                        columns: x => new { x.TenantId, x.ExpenseId },
                        principalTable: "Expenses",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Hawalas_TenantId_HawalaId",
                        columns: x => new { x.TenantId, x.HawalaId },
                        principalTable: "Hawalas",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_MoneyExchangeOperations_TenantId_MoneyExchangeOperationId",
                        columns: x => new { x.TenantId, x.MoneyExchangeOperationId },
                        principalTable: "MoneyExchangeOperations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Transactions_TenantId_TransactionId",
                        columns: x => new { x.TenantId, x.TransactionId },
                        principalTable: "Transactions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Transfers_TenantId_TransferId",
                        columns: x => new { x.TenantId, x.TransferId },
                        principalTable: "Transfers",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Accounts",
                columns: new[] { "Id", "AccountCode", "AccountName", "AccountType", "CorrespondentId", "CreatedAt", "CustomerId", "TenantId" },
                values: new object[,]
                {
                    { 1L, "1001", "صندوق", "Cash", null, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 1L },
                    { 2L, "1101", "بانک", "Bank", null, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 1L },
                    { 3L, "3001", "درآمد کمیسیون حواله", "Income", null, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 1L },
                    { 4L, "3002", "درآمد تبادل", "Income", null, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 1L },
                    { 5L, "4001", "هزینه دفتر", "Expense", null, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 1L },
                    { 6L, "5001", "سرمایه مالک", "Equity", null, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 1L }
                });

            migrationBuilder.InsertData(
                table: "Currencies",
                columns: new[] { "Id", "Code", "DecimalPlaces", "IsActive", "Name", "QuotationPriority", "Symbol", "TenantId" },
                values: new object[,]
                {
                    { 1L, "AFN", 2, true, "افغانی", 60, "؋", 1L },
                    { 2L, "USD", 2, true, "دالر امریکایی", 20, "$", 1L },
                    { 3L, "EUR", 2, true, "یورو", 10, "€", 1L },
                    { 4L, "AED", 2, true, "درهم عربی", 30, "د.إ", 1L },
                    { 5L, "IRR", 2, true, "ریال ایرانی", 50, "﷼", 1L },
                    { 6L, "PKR", 2, true, "روپیه پاکستانی", 40, "₨", 1L }
                });

            migrationBuilder.InsertData(
                table: "SubscriptionPlans",
                columns: new[] { "Id", "AnnualPrice", "Code", "CreatedAt", "CurrencyCode", "Description", "DisplayOrder", "IncludesAdvancedReports", "IncludesDocumentManagement", "IsActive", "MaxBranches", "MaxMonthlyTransactions", "MaxStorageBytes", "MaxUsers", "MonthlyPrice", "Name", "TrialDays", "UpdatedAt" },
                values: new object[] { 1L, 0m, "STANDARD", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "USD", "پلن پایه برای صرافی پیش‌فرض", 1, true, true, true, 5, 100000, 10737418240L, 25, 0m, "پلن استاندارد", 0, null });

            migrationBuilder.InsertData(
                table: "Tenants",
                columns: new[] { "Id", "ContactEmail", "ContactName", "ContactPhone", "CreatedAt", "IsActive", "IsArchived", "LastActivityAt", "LegalName", "Name" },
                values: new object[] { 1L, null, null, null, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, false, null, null, "صرافی پیش‌فرض" });

            migrationBuilder.InsertData(
                table: "Branches",
                columns: new[] { "Id", "Address", "Code", "CreatedAt", "Name", "PhoneNumber", "TenantId" },
                values: new object[] { 1L, "", "MAIN", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "شعبه اصلی", null, 1L });

            migrationBuilder.InsertData(
                table: "TenantSubscriptions",
                columns: new[] { "Id", "AdministrativeNote", "AgreedPrice", "AutoRenew", "BillingCycle", "CreatedAt", "CurrencyCode", "EndAt", "GracePeriodEndAt", "LastPaymentAt", "NextPaymentAt", "PlanId", "StartAt", "Status", "SuspensionReason", "TenantId", "TrialEndAt", "UpdatedAt" },
                values: new object[] { 1L, null, 0m, false, 12, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "USD", new DateTime(2036, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 1L, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 2, null, 1L, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_AccountBadehkarLimits_TenantId_AccountId_CurrencyId",
                table: "AccountBadehkarLimits",
                columns: new[] { "TenantId", "AccountId", "CurrencyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountBadehkarLimits_TenantId_CreatedBy",
                table: "AccountBadehkarLimits",
                columns: new[] { "TenantId", "CreatedBy" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountBadehkarLimits_TenantId_CurrencyId",
                table: "AccountBadehkarLimits",
                columns: new[] { "TenantId", "CurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountMoneyOperations_TenantId_AccountId",
                table: "AccountMoneyOperations",
                columns: new[] { "TenantId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountMoneyOperations_TenantId_CashOrBankAccountId",
                table: "AccountMoneyOperations",
                columns: new[] { "TenantId", "CashOrBankAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountMoneyOperations_TenantId_CurrencyId",
                table: "AccountMoneyOperations",
                columns: new[] { "TenantId", "CurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountMoneyOperations_TenantId_OperationDate",
                table: "AccountMoneyOperations",
                columns: new[] { "TenantId", "OperationDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_TenantId_AccountCode",
                table: "Accounts",
                columns: new[] { "TenantId", "AccountCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_TenantId_CorrespondentId",
                table: "Accounts",
                columns: new[] { "TenantId", "CorrespondentId" },
                unique: true,
                filter: "[CorrespondentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_TenantId_CustomerId",
                table: "Accounts",
                columns: new[] { "TenantId", "CustomerId" },
                unique: true,
                filter: "[CustomerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId_CreatedAt",
                table: "AuditLogs",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId_ProcessId",
                table: "AuditLogs",
                columns: new[] { "TenantId", "ProcessId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId_TableName_RecordId",
                table: "AuditLogs",
                columns: new[] { "TenantId", "TableName", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId_TransactionId",
                table: "AuditLogs",
                columns: new[] { "TenantId", "TransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId_UserId",
                table: "AuditLogs",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingNumberSequences_Year_Prefix",
                table: "BillingNumberSequences",
                columns: new[] { "Year", "Prefix" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Branches_TenantId_Code",
                table: "Branches",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CapitalInvestments_TenantId_CapitalAccountId",
                table: "CapitalInvestments",
                columns: new[] { "TenantId", "CapitalAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_CapitalInvestments_TenantId_CreatedAt",
                table: "CapitalInvestments",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CapitalInvestments_TenantId_CurrencyId",
                table: "CapitalInvestments",
                columns: new[] { "TenantId", "CurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_CapitalInvestments_TenantId_ProfitCurrencyId",
                table: "CapitalInvestments",
                columns: new[] { "TenantId", "ProfitCurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_CapitalInvestments_TenantId_ReceivingAccountId",
                table: "CapitalInvestments",
                columns: new[] { "TenantId", "ReceivingAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_CashBalanceAlertRecipients_TenantId_SettingId_UserId",
                table: "CashBalanceAlertRecipients",
                columns: new[] { "TenantId", "SettingId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashBalanceAlertRecipients_TenantId_UserId",
                table: "CashBalanceAlertRecipients",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_CashBalanceAlerts_TenantId_SettingId_IsActive",
                table: "CashBalanceAlerts",
                columns: new[] { "TenantId", "SettingId", "IsActive" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CashBalanceAlerts_TenantId_TriggeredAt",
                table: "CashBalanceAlerts",
                columns: new[] { "TenantId", "TriggeredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CashBalanceAlertSettings_TenantId_AccountId_CurrencyId",
                table: "CashBalanceAlertSettings",
                columns: new[] { "TenantId", "AccountId", "CurrencyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashBalanceAlertSettings_TenantId_CreatedBy",
                table: "CashBalanceAlertSettings",
                columns: new[] { "TenantId", "CreatedBy" });

            migrationBuilder.CreateIndex(
                name: "IX_CashBalanceAlertSettings_TenantId_CurrencyId",
                table: "CashBalanceAlertSettings",
                columns: new[] { "TenantId", "CurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_CashDailyBalances_TenantId_AccountId",
                table: "CashDailyBalances",
                columns: new[] { "TenantId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_CashDailyBalances_TenantId_CurrencyId",
                table: "CashDailyBalances",
                columns: new[] { "TenantId", "CurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_CashDailyBalances_TenantId_JournalDate_AccountId_CurrencyId",
                table: "CashDailyBalances",
                columns: new[] { "TenantId", "JournalDate", "AccountId", "CurrencyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanySettings_TenantId",
                table: "CompanySettings",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanySettings_TenantId_DefaultProfitCurrencyId",
                table: "CompanySettings",
                columns: new[] { "TenantId", "DefaultProfitCurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_Correspondents_TenantId_Code",
                table: "Correspondents",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Correspondents_TenantId_SettlementCurrencyId",
                table: "Correspondents",
                columns: new[] { "TenantId", "SettlementCurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentSettlementConversionHawalaItems_TenantId_ConversionId",
                table: "CorrespondentSettlementConversionHawalaItems",
                columns: new[] { "TenantId", "ConversionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentSettlementConversionHawalaItems_TenantId_HawalaId_SourceCurrencyId",
                table: "CorrespondentSettlementConversionHawalaItems",
                columns: new[] { "TenantId", "HawalaId", "SourceCurrencyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentSettlementConversionHawalaItems_TenantId_SourceCurrencyId",
                table: "CorrespondentSettlementConversionHawalaItems",
                columns: new[] { "TenantId", "SourceCurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentSettlementConversionHawalas_TenantId_ConversionId",
                table: "CorrespondentSettlementConversionHawalas",
                columns: new[] { "TenantId", "ConversionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentSettlementConversionHawalas_TenantId_HawalaId",
                table: "CorrespondentSettlementConversionHawalas",
                columns: new[] { "TenantId", "HawalaId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentSettlementConversionItems_TenantId_ConversionId_SourceCurrencyId",
                table: "CorrespondentSettlementConversionItems",
                columns: new[] { "TenantId", "ConversionId", "SourceCurrencyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentSettlementConversionItems_TenantId_SourceCurrencyId",
                table: "CorrespondentSettlementConversionItems",
                columns: new[] { "TenantId", "SourceCurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentSettlementConversions_TenantId_CorrespondentId_CreatedAt",
                table: "CorrespondentSettlementConversions",
                columns: new[] { "TenantId", "CorrespondentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentSettlementConversions_TenantId_CreatedBy",
                table: "CorrespondentSettlementConversions",
                columns: new[] { "TenantId", "CreatedBy" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentSettlementConversions_TenantId_TargetCurrencyId",
                table: "CorrespondentSettlementConversions",
                columns: new[] { "TenantId", "TargetCurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondentSettlementConversions_TenantId_TransactionId",
                table: "CorrespondentSettlementConversions",
                columns: new[] { "TenantId", "TransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Currencies_TenantId_Code",
                table: "Currencies",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId_CustomerCode",
                table: "Customers",
                columns: new[] { "TenantId", "CustomerCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_TenantId_AccountId",
                table: "Documents",
                columns: new[] { "TenantId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_TenantId_CorrespondentId",
                table: "Documents",
                columns: new[] { "TenantId", "CorrespondentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_TenantId_CustomerId",
                table: "Documents",
                columns: new[] { "TenantId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_TenantId_TransactionId",
                table: "Documents",
                columns: new[] { "TenantId", "TransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_TenantId_CreatedBy",
                table: "ExchangeRates",
                columns: new[] { "TenantId", "CreatedBy" });

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_TenantId_FromCurrencyId_ToCurrencyId_EffectiveDate",
                table: "ExchangeRates",
                columns: new[] { "TenantId", "FromCurrencyId", "ToCurrencyId", "EffectiveDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_TenantId_ToCurrencyId",
                table: "ExchangeRates",
                columns: new[] { "TenantId", "ToCurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_TenantId_CurrencyId",
                table: "Expenses",
                columns: new[] { "TenantId", "CurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_TenantId_ExpenseAccountId",
                table: "Expenses",
                columns: new[] { "TenantId", "ExpenseAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_TenantId_ExpenseDate",
                table: "Expenses",
                columns: new[] { "TenantId", "ExpenseDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_TenantId_PaidFromAccountId",
                table: "Expenses",
                columns: new[] { "TenantId", "PaidFromAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_TenantId_TransactionId",
                table: "Expenses",
                columns: new[] { "TenantId", "TransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_TenantId_AgentCommissionCurrencyId",
                table: "Hawalas",
                columns: new[] { "TenantId", "AgentCommissionCurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_TenantId_CancelledBy",
                table: "Hawalas",
                columns: new[] { "TenantId", "CancelledBy" });

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_TenantId_CommissionCurrencyId",
                table: "Hawalas",
                columns: new[] { "TenantId", "CommissionCurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_TenantId_CorrespondentId_HawalaType_Number",
                table: "Hawalas",
                columns: new[] { "TenantId", "CorrespondentId", "HawalaType", "Number" },
                unique: true,
                filter: "[CorrespondentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_TenantId_CreatedAt",
                table: "Hawalas",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_TenantId_CreatedBy",
                table: "Hawalas",
                columns: new[] { "TenantId", "CreatedBy" });

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_TenantId_FromCurrencyId",
                table: "Hawalas",
                columns: new[] { "TenantId", "FromCurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_TenantId_HawalaType_Status",
                table: "Hawalas",
                columns: new[] { "TenantId", "HawalaType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_TenantId_PaidBy",
                table: "Hawalas",
                columns: new[] { "TenantId", "PaidBy" });

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_TenantId_PaidFromAccountId",
                table: "Hawalas",
                columns: new[] { "TenantId", "PaidFromAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_TenantId_PaymentLocationId",
                table: "Hawalas",
                columns: new[] { "TenantId", "PaymentLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_TenantId_ReferenceNumber",
                table: "Hawalas",
                columns: new[] { "TenantId", "ReferenceNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_TenantId_SourceHawalaId",
                table: "Hawalas",
                columns: new[] { "TenantId", "SourceHawalaId" },
                unique: true,
                filter: "[SourceHawalaId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_TenantId_ToCurrencyId",
                table: "Hawalas",
                columns: new[] { "TenantId", "ToCurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_TenantId_AccountId_CurrencyId",
                table: "LedgerEntries",
                columns: new[] { "TenantId", "AccountId", "CurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_TenantId_AccountMoneyOperationId",
                table: "LedgerEntries",
                columns: new[] { "TenantId", "AccountMoneyOperationId" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_TenantId_CapitalInvestmentId",
                table: "LedgerEntries",
                columns: new[] { "TenantId", "CapitalInvestmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_TenantId_CreatedAt",
                table: "LedgerEntries",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_TenantId_CurrencyId",
                table: "LedgerEntries",
                columns: new[] { "TenantId", "CurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_TenantId_ExpenseId",
                table: "LedgerEntries",
                columns: new[] { "TenantId", "ExpenseId" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_TenantId_HawalaId",
                table: "LedgerEntries",
                columns: new[] { "TenantId", "HawalaId" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_TenantId_MoneyExchangeOperationId",
                table: "LedgerEntries",
                columns: new[] { "TenantId", "MoneyExchangeOperationId" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_TenantId_SettlementHawalaItemId",
                table: "LedgerEntries",
                columns: new[] { "TenantId", "SettlementHawalaItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_TenantId_TransactionId",
                table: "LedgerEntries",
                columns: new[] { "TenantId", "TransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_TenantId_TransferId",
                table: "LedgerEntries",
                columns: new[] { "TenantId", "TransferId" });

            migrationBuilder.CreateIndex(
                name: "IX_MoneyExchangeOperations_TenantId_CreatedAt",
                table: "MoneyExchangeOperations",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MoneyExchangeOperations_TenantId_FromAccountId",
                table: "MoneyExchangeOperations",
                columns: new[] { "TenantId", "FromAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_MoneyExchangeOperations_TenantId_FromCurrencyId",
                table: "MoneyExchangeOperations",
                columns: new[] { "TenantId", "FromCurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_MoneyExchangeOperations_TenantId_ProfitCurrencyId",
                table: "MoneyExchangeOperations",
                columns: new[] { "TenantId", "ProfitCurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_MoneyExchangeOperations_TenantId_RateBaseCurrencyId",
                table: "MoneyExchangeOperations",
                columns: new[] { "TenantId", "RateBaseCurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_MoneyExchangeOperations_TenantId_RateQuoteCurrencyId",
                table: "MoneyExchangeOperations",
                columns: new[] { "TenantId", "RateQuoteCurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_MoneyExchangeOperations_TenantId_ToAccountId",
                table: "MoneyExchangeOperations",
                columns: new[] { "TenantId", "ToAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_MoneyExchangeOperations_TenantId_ToCurrencyId",
                table: "MoneyExchangeOperations",
                columns: new[] { "TenantId", "ToCurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLocations_TenantId_CorrespondentId",
                table: "PaymentLocations",
                columns: new[] { "TenantId", "CorrespondentId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLocations_TenantId_CreatedBy",
                table: "PaymentLocations",
                columns: new[] { "TenantId", "CreatedBy" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLocations_TenantId_UpdatedBy",
                table: "PaymentLocations",
                columns: new[] { "TenantId", "UpdatedBy" });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAuditLogs_CreatedAt_Action",
                table: "PlatformAuditLogs",
                columns: new[] { "CreatedAt", "Action" });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformAuditLogs_TenantId_CreatedAt",
                table: "PlatformAuditLogs",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleClaims_RoleId",
                table: "RoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true,
                filter: "[Name] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "Roles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

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

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionInvoiceItems_InvoiceId_SortOrder",
                table: "SubscriptionInvoiceItems",
                columns: new[] { "InvoiceId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionInvoices_CurrencyCode_IssuedAt",
                table: "SubscriptionInvoices",
                columns: new[] { "CurrencyCode", "IssuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionInvoices_InvoiceNumber",
                table: "SubscriptionInvoices",
                column: "InvoiceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionInvoices_Status_DueAt",
                table: "SubscriptionInvoices",
                columns: new[] { "Status", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionInvoices_SubscriptionId",
                table: "SubscriptionInvoices",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionInvoices_TenantId_Status_DueAt",
                table: "SubscriptionInvoices",
                columns: new[] { "TenantId", "Status", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPayments_InvoiceId",
                table: "SubscriptionPayments",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPayments_ProviderTransactionId",
                table: "SubscriptionPayments",
                column: "ProviderTransactionId",
                unique: true,
                filter: "[ProviderTransactionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPayments_ReferenceNumber",
                table: "SubscriptionPayments",
                column: "ReferenceNumber",
                unique: true,
                filter: "[ReferenceNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPayments_SubscriptionId_Status_DueAt",
                table: "SubscriptionPayments",
                columns: new[] { "SubscriptionId", "Status", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlans_Code",
                table: "SubscriptionPlans",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlans_IsActive_DisplayOrder",
                table: "SubscriptionPlans",
                columns: new[] { "IsActive", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantSubscriptions_NextPaymentAt",
                table: "TenantSubscriptions",
                column: "NextPaymentAt");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSubscriptions_PlanId",
                table: "TenantSubscriptions",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSubscriptions_Status_EndAt",
                table: "TenantSubscriptions",
                columns: new[] { "Status", "EndAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantSubscriptions_TenantId_Status_EndAt",
                table: "TenantSubscriptions",
                columns: new[] { "TenantId", "Status", "EndAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantUsageSnapshots_PeriodStart",
                table: "TenantUsageSnapshots",
                column: "PeriodStart");

            migrationBuilder.CreateIndex(
                name: "IX_TenantUsageSnapshots_TenantId_PeriodStart",
                table: "TenantUsageSnapshots",
                columns: new[] { "TenantId", "PeriodStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransactionDetails_TenantId_AgentCommissionCurrencyId",
                table: "TransactionDetails",
                columns: new[] { "TenantId", "AgentCommissionCurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionDetails_TenantId_CommissionCurrencyId",
                table: "TransactionDetails",
                columns: new[] { "TenantId", "CommissionCurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionDetails_TenantId_CorrespondentId",
                table: "TransactionDetails",
                columns: new[] { "TenantId", "CorrespondentId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionDetails_TenantId_FromCurrencyId",
                table: "TransactionDetails",
                columns: new[] { "TenantId", "FromCurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionDetails_TenantId_ToCurrencyId",
                table: "TransactionDetails",
                columns: new[] { "TenantId", "ToCurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionDetails_TenantId_TransactionId",
                table: "TransactionDetails",
                columns: new[] { "TenantId", "TransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TenantId_BranchId",
                table: "Transactions",
                columns: new[] { "TenantId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TenantId_CancelledBy",
                table: "Transactions",
                columns: new[] { "TenantId", "CancelledBy" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TenantId_CreatedAt_Status",
                table: "Transactions",
                columns: new[] { "TenantId", "CreatedAt", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TenantId_CreatedBy",
                table: "Transactions",
                columns: new[] { "TenantId", "CreatedBy" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TenantId_CustomerId",
                table: "Transactions",
                columns: new[] { "TenantId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TenantId_HawalaId",
                table: "Transactions",
                columns: new[] { "TenantId", "HawalaId" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TenantId_ReversedTransactionId",
                table: "Transactions",
                columns: new[] { "TenantId", "ReversedTransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TenantId_TransactionNo",
                table: "Transactions",
                columns: new[] { "TenantId", "TransactionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_TenantId_CurrencyId",
                table: "Transfers",
                columns: new[] { "TenantId", "CurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_TenantId_FromAccountId",
                table: "Transfers",
                columns: new[] { "TenantId", "FromAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_TenantId_ProfitCurrencyId",
                table: "Transfers",
                columns: new[] { "TenantId", "ProfitCurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_TenantId_ToAccountId",
                table: "Transfers",
                columns: new[] { "TenantId", "ToAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_TenantId_TransactionId",
                table: "Transfers",
                columns: new[] { "TenantId", "TransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserClaims_UserId",
                table: "UserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserLogins_UserId",
                table: "UserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                table: "UserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "Users",
                column: "NormalizedEmail",
                unique: true,
                filter: "[NormalizedEmail] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Users_LocalUserName",
                table: "Users",
                column: "LocalUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_BranchId",
                table: "Users",
                columns: new[] { "TenantId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "Users",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountBadehkarLimits");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "BillingNumberSequences");

            migrationBuilder.DropTable(
                name: "CashBalanceAlertRecipients");

            migrationBuilder.DropTable(
                name: "CashBalanceAlerts");

            migrationBuilder.DropTable(
                name: "CashDailyBalances");

            migrationBuilder.DropTable(
                name: "CompanySettings");

            migrationBuilder.DropTable(
                name: "CorrespondentSettlementConversionHawalas");

            migrationBuilder.DropTable(
                name: "CorrespondentSettlementConversionItems");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "ExchangeRates");

            migrationBuilder.DropTable(
                name: "LedgerEntries");

            migrationBuilder.DropTable(
                name: "PlatformAuditLogs");

            migrationBuilder.DropTable(
                name: "RoleClaims");

            migrationBuilder.DropTable(
                name: "SaasAutomationRuns");

            migrationBuilder.DropTable(
                name: "SaasAutomationSettings");

            migrationBuilder.DropTable(
                name: "SaasNotifications");

            migrationBuilder.DropTable(
                name: "SubscriptionInvoiceItems");

            migrationBuilder.DropTable(
                name: "SubscriptionPayments");

            migrationBuilder.DropTable(
                name: "TenantUsageSnapshots");

            migrationBuilder.DropTable(
                name: "TransactionDetails");

            migrationBuilder.DropTable(
                name: "UserClaims");

            migrationBuilder.DropTable(
                name: "UserLogins");

            migrationBuilder.DropTable(
                name: "UserRoles");

            migrationBuilder.DropTable(
                name: "UserTokens");

            migrationBuilder.DropTable(
                name: "CashBalanceAlertSettings");

            migrationBuilder.DropTable(
                name: "AccountMoneyOperations");

            migrationBuilder.DropTable(
                name: "CapitalInvestments");

            migrationBuilder.DropTable(
                name: "CorrespondentSettlementConversionHawalaItems");

            migrationBuilder.DropTable(
                name: "Expenses");

            migrationBuilder.DropTable(
                name: "MoneyExchangeOperations");

            migrationBuilder.DropTable(
                name: "Transfers");

            migrationBuilder.DropTable(
                name: "SubscriptionInvoices");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "CorrespondentSettlementConversions");

            migrationBuilder.DropTable(
                name: "TenantSubscriptions");

            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "SubscriptionPlans");

            migrationBuilder.DropTable(
                name: "Hawalas");

            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.DropTable(
                name: "PaymentLocations");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "Correspondents");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Currencies");

            migrationBuilder.DropTable(
                name: "Branches");

            migrationBuilder.DropTable(
                name: "Tenants");
        }
    }
}
