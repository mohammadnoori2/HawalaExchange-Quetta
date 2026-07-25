using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HawalaExchange.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AccountName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AccountType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReferenceType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ReferenceId = table.Column<long>(type: "bigint", nullable: true),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
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
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Branches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
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
                });

            migrationBuilder.CreateTable(
                name: "CompanySettings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LogoPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    WhatsAppNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TelegramUserName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FooterNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanySettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Correspondents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    Remarks = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Correspondents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Currencies",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DecimalPlaces = table.Column<int>(type: "int", nullable: false, defaultValue: 2),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Currencies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
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
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrialBalances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    AccountCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AccountName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TotalDebit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalCredit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BalanceType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    AsOfDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrialBalances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrialBalances_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
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
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FullName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BranchId = table.Column<long>(type: "bigint", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUsers_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommissionReports",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BranchId = table.Column<long>(type: "bigint", nullable: false),
                    BranchName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TotalCommission = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalAgentCommission = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    NetCommission = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommissionReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommissionReports_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DailyReports",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BranchId = table.Column<long>(type: "bigint", nullable: false),
                    BranchName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TotalTransactions = table.Column<int>(type: "int", nullable: false),
                    TotalSendAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalReceiveAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalCommission = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalExpenses = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    NetIncome = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyReports", x => x.Id);
                    table.CheckConstraint("CK_DailyReport_TotalReceiveAmount_NonNegative", "[TotalReceiveAmount] >= 0");
                    table.CheckConstraint("CK_DailyReport_TotalSendAmount_NonNegative", "[TotalSendAmount] >= 0");
                    table.ForeignKey(
                        name: "FK_DailyReports_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccountMoneyOperations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
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
                    table.ForeignKey(
                        name: "FK_AccountMoneyOperations_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountMoneyOperations_Accounts_CashOrBankAccountId",
                        column: x => x.CashOrBankAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountMoneyOperations_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CapitalInvestments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
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
                    table.ForeignKey(
                        name: "FK_CapitalInvestments_Accounts_CapitalAccountId",
                        column: x => x.CapitalAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CapitalInvestments_Accounts_ReceivingAccountId",
                        column: x => x.ReceivingAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CapitalInvestments_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CapitalInvestments_Currencies_ProfitCurrencyId",
                        column: x => x.ProfitCurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MoneyExchangeOperations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExchangeDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FromAccountId = table.Column<long>(type: "bigint", nullable: false),
                    FromCurrencyId = table.Column<long>(type: "bigint", nullable: false),
                    FromAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ToAccountId = table.Column<long>(type: "bigint", nullable: false),
                    ToCurrencyId = table.Column<long>(type: "bigint", nullable: false),
                    ToAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    OperationType = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "Treasury"),
                    ProfitCurrencyId = table.Column<long>(type: "bigint", nullable: true),
                    CommissionAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ExternalFeeAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CostAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RealizedProfit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
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
                    table.ForeignKey(
                        name: "FK_MoneyExchangeOperations_Accounts_FromAccountId",
                        column: x => x.FromAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MoneyExchangeOperations_Accounts_ToAccountId",
                        column: x => x.ToAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MoneyExchangeOperations_Currencies_FromCurrencyId",
                        column: x => x.FromCurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MoneyExchangeOperations_Currencies_ProfitCurrencyId",
                        column: x => x.ProfitCurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MoneyExchangeOperations_Currencies_ToCurrencyId",
                        column: x => x.ToCurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccountBadehkarLimits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
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
                    table.ForeignKey(
                        name: "FK_AccountBadehkarLimits_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountBadehkarLimits_AspNetUsers_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountBadehkarLimits_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
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
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    RoleId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExchangeRates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FromCurrencyId = table.Column<long>(type: "bigint", nullable: false),
                    ToCurrencyId = table.Column<long>(type: "bigint", nullable: false),
                    BuyRate = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    SellRate = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    CurrencyId = table.Column<long>(type: "bigint", nullable: true),
                    CurrencyId1 = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExchangeRates_AspNetUsers_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExchangeRates_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ExchangeRates_Currencies_CurrencyId1",
                        column: x => x.CurrencyId1,
                        principalTable: "Currencies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ExchangeRates_Currencies_FromCurrencyId",
                        column: x => x.FromCurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExchangeRates_Currencies_ToCurrencyId",
                        column: x => x.ToCurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentLocations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
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
                    table.ForeignKey(
                        name: "FK_PaymentLocations_AspNetUsers_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PaymentLocations_AspNetUsers_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Hawalas",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
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
                    table.CheckConstraint("CK_Hawala_CommissionAmount_NonNegative_IfNotNull", "[CommissionAmount] IS NULL OR [CommissionAmount] >= 0");
                    table.CheckConstraint("CK_Hawala_ExchangeRate_Positive_IfNotNull", "[ExchangeRate] IS NULL OR [ExchangeRate] > 0");
                    table.CheckConstraint("CK_Hawala_FromAmount_Positive", "[FromAmount] > 0");
                    table.CheckConstraint("CK_Hawala_HawalaType_Valid", "[HawalaType] IN ('HawalaSend', 'HawalaReceive', 'HawalaOther')");
                    table.CheckConstraint("CK_Hawala_Status_Valid", "[Status] IN ('Pending', 'Paid', 'Cancel')");
                    table.CheckConstraint("CK_Hawala_ToAmount_Positive_IfNotNull", "[ToAmount] IS NULL OR [ToAmount] > 0");
                    table.ForeignKey(
                        name: "FK_Hawalas_Accounts_PaidFromAccountId",
                        column: x => x.PaidFromAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Hawalas_AspNetUsers_CancelledBy",
                        column: x => x.CancelledBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Hawalas_AspNetUsers_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Hawalas_AspNetUsers_PaidBy",
                        column: x => x.PaidBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Hawalas_Correspondents_CorrespondentId",
                        column: x => x.CorrespondentId,
                        principalTable: "Correspondents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Hawalas_Currencies_AgentCommissionCurrencyId",
                        column: x => x.AgentCommissionCurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Hawalas_Currencies_CommissionCurrencyId",
                        column: x => x.CommissionCurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Hawalas_Currencies_FromCurrencyId",
                        column: x => x.FromCurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Hawalas_Currencies_ToCurrencyId",
                        column: x => x.ToCurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Hawalas_Hawalas_SourceHawalaId",
                        column: x => x.SourceHawalaId,
                        principalTable: "Hawalas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Hawalas_PaymentLocations_PaymentLocationId",
                        column: x => x.PaymentLocationId,
                        principalTable: "PaymentLocations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransactionNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TransactionType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    BranchId = table.Column<long>(type: "bigint", nullable: false),
                    CustomerId = table.Column<long>(type: "bigint", nullable: true),
                    CustomerFullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CancelledBy = table.Column<long>(type: "bigint", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReversedTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    HawalaId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transactions_AspNetUsers_CancelledBy",
                        column: x => x.CancelledBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transactions_AspNetUsers_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transactions_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transactions_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transactions_Hawalas_HawalaId",
                        column: x => x.HawalaId,
                        principalTable: "Hawalas",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Transactions_Transactions_ReversedTransactionId",
                        column: x => x.ReversedTransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
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
                    table.ForeignKey(
                        name: "FK_AuditLogs_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuditLogs_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<long>(type: "bigint", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TransactionId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Documents_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
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
                    CurrencyId1 = table.Column<long>(type: "bigint", nullable: true),
                    TransactionId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Expenses_Accounts_ExpenseAccountId",
                        column: x => x.ExpenseAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_Accounts_PaidFromAccountId",
                        column: x => x.PaidFromAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_Currencies_CurrencyId1",
                        column: x => x.CurrencyId1,
                        principalTable: "Currencies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Expenses_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TransactionDetails",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
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
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CurrencyId = table.Column<long>(type: "bigint", nullable: true),
                    CurrencyId1 = table.Column<long>(type: "bigint", nullable: true),
                    CurrencyId2 = table.Column<long>(type: "bigint", nullable: true),
                    CurrencyId3 = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransactionDetails_Correspondents_CorrespondentId",
                        column: x => x.CorrespondentId,
                        principalTable: "Correspondents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransactionDetails_Currencies_AgentCommissionCurrencyId",
                        column: x => x.AgentCommissionCurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransactionDetails_Currencies_CommissionCurrencyId",
                        column: x => x.CommissionCurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransactionDetails_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TransactionDetails_Currencies_CurrencyId1",
                        column: x => x.CurrencyId1,
                        principalTable: "Currencies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TransactionDetails_Currencies_CurrencyId2",
                        column: x => x.CurrencyId2,
                        principalTable: "Currencies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TransactionDetails_Currencies_CurrencyId3",
                        column: x => x.CurrencyId3,
                        principalTable: "Currencies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TransactionDetails_Currencies_FromCurrencyId",
                        column: x => x.FromCurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransactionDetails_Currencies_ToCurrencyId",
                        column: x => x.ToCurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransactionDetails_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransactionReports",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransactionNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TransactionType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SenderName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ReceiverName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FromCurrency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    FromAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ToCurrency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ToAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Commission = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReportGeneratedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    TransactionId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransactionReports_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Transfers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FromAccountId = table.Column<long>(type: "bigint", nullable: false),
                    ToAccountId = table.Column<long>(type: "bigint", nullable: false),
                    CurrencyId = table.Column<long>(type: "bigint", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TransferMethod = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CurrencyId1 = table.Column<long>(type: "bigint", nullable: true),
                    TransactionId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transfers_Accounts_FromAccountId",
                        column: x => x.FromAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transfers_Accounts_ToAccountId",
                        column: x => x.ToAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transfers_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transfers_Currencies_CurrencyId1",
                        column: x => x.CurrencyId1,
                        principalTable: "Currencies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Transfers_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LedgerEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
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
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerEntries", x => x.Id);
                    table.CheckConstraint("CK_LedgerEntries_BadehKar_NonNegative", "[BadehKar] >= 0");
                    table.CheckConstraint("CK_LedgerEntries_OnlyOneSide", "([TalabKar] > 0 AND [BadehKar] = 0) OR ([TalabKar] = 0 AND [BadehKar] > 0)");
                    table.CheckConstraint("CK_LedgerEntries_TalabKar_NonNegative", "[TalabKar] >= 0");
                    table.ForeignKey(
                        name: "FK_LedgerEntries_AccountMoneyOperations_AccountMoneyOperationId",
                        column: x => x.AccountMoneyOperationId,
                        principalTable: "AccountMoneyOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_CapitalInvestments_CapitalInvestmentId",
                        column: x => x.CapitalInvestmentId,
                        principalTable: "CapitalInvestments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Expenses_ExpenseId",
                        column: x => x.ExpenseId,
                        principalTable: "Expenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Hawalas_HawalaId",
                        column: x => x.HawalaId,
                        principalTable: "Hawalas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_MoneyExchangeOperations_MoneyExchangeOperationId",
                        column: x => x.MoneyExchangeOperationId,
                        principalTable: "MoneyExchangeOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Transfers_TransferId",
                        column: x => x.TransferId,
                        principalTable: "Transfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Accounts",
                columns: new[] { "Id", "AccountCode", "AccountName", "AccountType", "CreatedAt", "ReferenceId", "ReferenceType" },
                values: new object[,]
                {
                    { 1L, "1001", "صندوق", "Cash", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null },
                    { 2L, "1101", "بانک", "Bank", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null },
                    { 3L, "3001", "درآمد کمیسیون حواله", "Income", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null },
                    { 4L, "3002", "درآمد تبادل", "Income", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null },
                    { 5L, "4001", "هزینه دفتر", "Expense", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null },
                    { 6L, "5001", "سرمایه مالک", "Equity", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null }
                });

            migrationBuilder.InsertData(
                table: "Branches",
                columns: new[] { "Id", "Address", "Code", "CreatedAt", "Name", "PhoneNumber" },
                values: new object[] { 1L, "", "MAIN", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "شعبه اصلی", null });

            migrationBuilder.InsertData(
                table: "Currencies",
                columns: new[] { "Id", "Code", "DecimalPlaces", "IsActive", "Name", "Symbol" },
                values: new object[,]
                {
                    { 1L, "AFN", 2, true, "افغانی", "؋" },
                    { 2L, "USD", 2, true, "دالر امریکایی", "$" },
                    { 3L, "EUR", 2, true, "یورو", "€" },
                    { 4L, "AED", 2, true, "درهم عربی", "د.إ" },
                    { 5L, "IRR", 2, true, "ریال ایرانی", "﷼" },
                    { 6L, "PKR", 2, true, "روپیه پاکستانی", "₨" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountBadehkarLimits_AccountId_CurrencyId",
                table: "AccountBadehkarLimits",
                columns: new[] { "AccountId", "CurrencyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountBadehkarLimits_CreatedBy",
                table: "AccountBadehkarLimits",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_AccountBadehkarLimits_CurrencyId",
                table: "AccountBadehkarLimits",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountMoneyOperations_AccountId",
                table: "AccountMoneyOperations",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountMoneyOperations_CashOrBankAccountId",
                table: "AccountMoneyOperations",
                column: "CashOrBankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountMoneyOperations_CurrencyId",
                table: "AccountMoneyOperations",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_AccountCode",
                table: "Accounts",
                column: "AccountCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_BranchId",
                table: "AspNetUsers",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TableName_RecordId",
                table: "AuditLogs",
                columns: new[] { "TableName", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TransactionId",
                table: "AuditLogs",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Branches_Code",
                table: "Branches",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CapitalInvestments_CapitalAccountId",
                table: "CapitalInvestments",
                column: "CapitalAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CapitalInvestments_CurrencyId",
                table: "CapitalInvestments",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_CapitalInvestments_ProfitCurrencyId",
                table: "CapitalInvestments",
                column: "ProfitCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_CapitalInvestments_ReceivingAccountId",
                table: "CapitalInvestments",
                column: "ReceivingAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionReports_BranchId",
                table: "CommissionReports",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionReports_Date_BranchId",
                table: "CommissionReports",
                columns: new[] { "Date", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_Correspondents_Code",
                table: "Correspondents",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Currencies_Code",
                table: "Currencies",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CustomerCode",
                table: "Customers",
                column: "CustomerCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyReports_BranchId",
                table: "DailyReports",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyReports_Date_BranchId",
                table: "DailyReports",
                columns: new[] { "Date", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_EntityType_EntityId",
                table: "Documents",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_TransactionId",
                table: "Documents",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_CreatedBy",
                table: "ExchangeRates",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_CurrencyId",
                table: "ExchangeRates",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_CurrencyId1",
                table: "ExchangeRates",
                column: "CurrencyId1");

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_FromCurrencyId_ToCurrencyId_EffectiveDate",
                table: "ExchangeRates",
                columns: new[] { "FromCurrencyId", "ToCurrencyId", "EffectiveDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_ToCurrencyId",
                table: "ExchangeRates",
                column: "ToCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CurrencyId",
                table: "Expenses",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CurrencyId1",
                table: "Expenses",
                column: "CurrencyId1");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ExpenseAccountId",
                table: "Expenses",
                column: "ExpenseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_PaidFromAccountId",
                table: "Expenses",
                column: "PaidFromAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_TransactionId",
                table: "Expenses",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_AgentCommissionCurrencyId",
                table: "Hawalas",
                column: "AgentCommissionCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_CancelledBy",
                table: "Hawalas",
                column: "CancelledBy");

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_CommissionCurrencyId",
                table: "Hawalas",
                column: "CommissionCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_CorrespondentId_HawalaType_Number",
                table: "Hawalas",
                columns: new[] { "CorrespondentId", "HawalaType", "Number" },
                unique: true,
                filter: "[CorrespondentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_CreatedAt",
                table: "Hawalas",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_CreatedBy",
                table: "Hawalas",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_FromCurrencyId",
                table: "Hawalas",
                column: "FromCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_HawalaType_Status",
                table: "Hawalas",
                columns: new[] { "HawalaType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_PaidBy",
                table: "Hawalas",
                column: "PaidBy");

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_PaidFromAccountId",
                table: "Hawalas",
                column: "PaidFromAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_PaymentLocationId",
                table: "Hawalas",
                column: "PaymentLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_ReferenceNumber",
                table: "Hawalas",
                column: "ReferenceNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_SourceHawalaId",
                table: "Hawalas",
                column: "SourceHawalaId",
                unique: true,
                filter: "[SourceHawalaId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Hawalas_ToCurrencyId",
                table: "Hawalas",
                column: "ToCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_AccountId_CurrencyId",
                table: "LedgerEntries",
                columns: new[] { "AccountId", "CurrencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_AccountMoneyOperationId",
                table: "LedgerEntries",
                column: "AccountMoneyOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_CapitalInvestmentId",
                table: "LedgerEntries",
                column: "CapitalInvestmentId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_CurrencyId",
                table: "LedgerEntries",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_ExpenseId",
                table: "LedgerEntries",
                column: "ExpenseId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_HawalaId",
                table: "LedgerEntries",
                column: "HawalaId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_MoneyExchangeOperationId",
                table: "LedgerEntries",
                column: "MoneyExchangeOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_TransactionId",
                table: "LedgerEntries",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_TransferId",
                table: "LedgerEntries",
                column: "TransferId");

            migrationBuilder.CreateIndex(
                name: "IX_MoneyExchangeOperations_FromAccountId",
                table: "MoneyExchangeOperations",
                column: "FromAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MoneyExchangeOperations_FromCurrencyId",
                table: "MoneyExchangeOperations",
                column: "FromCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_MoneyExchangeOperations_ProfitCurrencyId",
                table: "MoneyExchangeOperations",
                column: "ProfitCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_MoneyExchangeOperations_ToAccountId",
                table: "MoneyExchangeOperations",
                column: "ToAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MoneyExchangeOperations_ToCurrencyId",
                table: "MoneyExchangeOperations",
                column: "ToCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLocations_CreatedBy",
                table: "PaymentLocations",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLocations_UpdatedBy",
                table: "PaymentLocations",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionDetails_AgentCommissionCurrencyId",
                table: "TransactionDetails",
                column: "AgentCommissionCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionDetails_CommissionCurrencyId",
                table: "TransactionDetails",
                column: "CommissionCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionDetails_CorrespondentId",
                table: "TransactionDetails",
                column: "CorrespondentId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionDetails_CurrencyId",
                table: "TransactionDetails",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionDetails_CurrencyId1",
                table: "TransactionDetails",
                column: "CurrencyId1");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionDetails_CurrencyId2",
                table: "TransactionDetails",
                column: "CurrencyId2");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionDetails_CurrencyId3",
                table: "TransactionDetails",
                column: "CurrencyId3");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionDetails_FromCurrencyId",
                table: "TransactionDetails",
                column: "FromCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionDetails_ToCurrencyId",
                table: "TransactionDetails",
                column: "ToCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionDetails_TransactionId",
                table: "TransactionDetails",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionReports_CreatedAt",
                table: "TransactionReports",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionReports_TransactionId",
                table: "TransactionReports",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionReports_TransactionNo",
                table: "TransactionReports",
                column: "TransactionNo");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_BranchId",
                table: "Transactions",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CancelledBy",
                table: "Transactions",
                column: "CancelledBy");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CreatedBy",
                table: "Transactions",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CustomerId",
                table: "Transactions",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_HawalaId",
                table: "Transactions",
                column: "HawalaId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ReversedTransactionId",
                table: "Transactions",
                column: "ReversedTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TransactionNo",
                table: "Transactions",
                column: "TransactionNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_CurrencyId",
                table: "Transfers",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_CurrencyId1",
                table: "Transfers",
                column: "CurrencyId1");

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_FromAccountId",
                table: "Transfers",
                column: "FromAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_ToAccountId",
                table: "Transfers",
                column: "ToAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_TransactionId",
                table: "Transfers",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_TrialBalances_AccountId",
                table: "TrialBalances",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_TrialBalances_AsOfDate_AccountId",
                table: "TrialBalances",
                columns: new[] { "AsOfDate", "AccountId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountBadehkarLimits");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "CommissionReports");

            migrationBuilder.DropTable(
                name: "CompanySettings");

            migrationBuilder.DropTable(
                name: "DailyReports");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "ExchangeRates");

            migrationBuilder.DropTable(
                name: "LedgerEntries");

            migrationBuilder.DropTable(
                name: "TransactionDetails");

            migrationBuilder.DropTable(
                name: "TransactionReports");

            migrationBuilder.DropTable(
                name: "TrialBalances");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

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
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "Hawalas");

            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.DropTable(
                name: "Correspondents");

            migrationBuilder.DropTable(
                name: "Currencies");

            migrationBuilder.DropTable(
                name: "PaymentLocations");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Branches");
        }
    }
}
