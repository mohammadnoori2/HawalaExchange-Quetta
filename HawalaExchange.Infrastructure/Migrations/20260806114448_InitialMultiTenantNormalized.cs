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
                    Remarks = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Correspondents", x => x.Id);
                    table.UniqueConstraint("AK_Correspondents_TenantId_Id", x => new { x.TenantId, x.Id });
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
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
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
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<long>(type: "bigint", nullable: false),
                    LocalUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BranchId = table.Column<long>(type: "bigint", nullable: false),
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
                        name: "FK_Transfers_Transactions_TenantId_TransactionId",
                        columns: x => new { x.TenantId, x.TransactionId },
                        principalTable: "Transactions",
                        principalColumns: new[] { "TenantId", "Id" });
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
                table: "Tenants",
                columns: new[] { "Id", "CreatedAt", "IsActive", "Name" },
                values: new object[] { 1L, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "صرافی پیش‌فرض" });

            migrationBuilder.InsertData(
                table: "Branches",
                columns: new[] { "Id", "Address", "Code", "CreatedAt", "Name", "PhoneNumber", "TenantId" },
                values: new object[] { 1L, "", "MAIN", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "شعبه اصلی", null, 1L });

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
                name: "IX_PaymentLocations_TenantId_CreatedBy",
                table: "PaymentLocations",
                columns: new[] { "TenantId", "CreatedBy" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLocations_TenantId_UpdatedBy",
                table: "PaymentLocations",
                columns: new[] { "TenantId", "UpdatedBy" });

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
                name: "CashDailyBalances");

            migrationBuilder.DropTable(
                name: "CompanySettings");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "ExchangeRates");

            migrationBuilder.DropTable(
                name: "LedgerEntries");

            migrationBuilder.DropTable(
                name: "RoleClaims");

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
                name: "AccountMoneyOperations");

            migrationBuilder.DropTable(
                name: "CapitalInvestments");

            migrationBuilder.DropTable(
                name: "Expenses");

            migrationBuilder.DropTable(
                name: "MoneyExchangeOperations");

            migrationBuilder.DropTable(
                name: "Transfers");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "Hawalas");

            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.DropTable(
                name: "Currencies");

            migrationBuilder.DropTable(
                name: "PaymentLocations");

            migrationBuilder.DropTable(
                name: "Correspondents");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Branches");

            migrationBuilder.DropTable(
                name: "Tenants");
        }
    }
}
