using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HawalaExchange.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCanonicalRatesAndExchangeLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeProfitAmount",
                table: "MoneyExchangeOperations",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "InventoryCostDecrease",
                table: "MoneyExchangeOperations",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "InventoryCostIncrease",
                table: "MoneyExchangeOperations",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "RateBaseCurrencyId",
                table: "MoneyExchangeOperations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RateQuoteCurrencyId",
                table: "MoneyExchangeOperations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShortLiabilityDecrease",
                table: "MoneyExchangeOperations",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ShortLiabilityIncrease",
                table: "MoneyExchangeOperations",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "QuotationPriority",
                table: "Currencies",
                type: "int",
                nullable: false,
                defaultValue: 1000);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [Accounts] WHERE [AccountCode] = N'1201')
                    INSERT INTO [Accounts] ([AccountCode], [AccountName], [AccountType], [IsArchived], [CreatedAt])
                    VALUES (N'1201', N'موجودی ارز به بهای تمام‌شده', N'Asset', 0, SYSUTCDATETIME());

                IF NOT EXISTS (SELECT 1 FROM [Accounts] WHERE [AccountCode] = N'2101')
                    INSERT INTO [Accounts] ([AccountCode], [AccountName], [AccountType], [IsArchived], [CreatedAt])
                    VALUES (N'2101', N'تعهد فروش ارز', N'Liability', 0, SYSUTCDATETIME());
                """);

            migrationBuilder.UpdateData(
                table: "Currencies",
                keyColumn: "Id",
                keyValue: 1L,
                column: "QuotationPriority",
                value: 60);

            migrationBuilder.UpdateData(
                table: "Currencies",
                keyColumn: "Id",
                keyValue: 2L,
                column: "QuotationPriority",
                value: 20);

            migrationBuilder.UpdateData(
                table: "Currencies",
                keyColumn: "Id",
                keyValue: 3L,
                column: "QuotationPriority",
                value: 10);

            migrationBuilder.UpdateData(
                table: "Currencies",
                keyColumn: "Id",
                keyValue: 4L,
                column: "QuotationPriority",
                value: 30);

            migrationBuilder.UpdateData(
                table: "Currencies",
                keyColumn: "Id",
                keyValue: 5L,
                column: "QuotationPriority",
                value: 50);

            migrationBuilder.UpdateData(
                table: "Currencies",
                keyColumn: "Id",
                keyValue: 6L,
                column: "QuotationPriority",
                value: 40);

            migrationBuilder.Sql("""
                UPDATE m
                SET
                    [RateBaseCurrencyId] =
                        CASE WHEN f.[QuotationPriority] < t.[QuotationPriority]
                                  OR (f.[QuotationPriority] = t.[QuotationPriority] AND f.[Code] < t.[Code])
                             THEN m.[FromCurrencyId] ELSE m.[ToCurrencyId] END,
                    [RateQuoteCurrencyId] =
                        CASE WHEN f.[QuotationPriority] < t.[QuotationPriority]
                                  OR (f.[QuotationPriority] = t.[QuotationPriority] AND f.[Code] < t.[Code])
                             THEN m.[ToCurrencyId] ELSE m.[FromCurrencyId] END,
                    [ExchangeRate] =
                        CASE WHEN f.[QuotationPriority] < t.[QuotationPriority]
                                  OR (f.[QuotationPriority] = t.[QuotationPriority] AND f.[Code] < t.[Code])
                             THEN m.[ToAmount] / NULLIF(m.[FromAmount], 0)
                             ELSE m.[FromAmount] / NULLIF(m.[ToAmount], 0) END
                FROM [MoneyExchangeOperations] m
                INNER JOIN [Currencies] f ON f.[Id] = m.[FromCurrencyId]
                INNER JOIN [Currencies] t ON t.[Id] = m.[ToCurrencyId]
                WHERE m.[FromAmount] > 0 AND m.[ToAmount] > 0;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_MoneyExchangeOperations_RateBaseCurrencyId",
                table: "MoneyExchangeOperations",
                column: "RateBaseCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_MoneyExchangeOperations_RateQuoteCurrencyId",
                table: "MoneyExchangeOperations",
                column: "RateQuoteCurrencyId");

            migrationBuilder.AddForeignKey(
                name: "FK_MoneyExchangeOperations_Currencies_RateBaseCurrencyId",
                table: "MoneyExchangeOperations",
                column: "RateBaseCurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MoneyExchangeOperations_Currencies_RateQuoteCurrencyId",
                table: "MoneyExchangeOperations",
                column: "RateQuoteCurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MoneyExchangeOperations_Currencies_RateBaseCurrencyId",
                table: "MoneyExchangeOperations");

            migrationBuilder.DropForeignKey(
                name: "FK_MoneyExchangeOperations_Currencies_RateQuoteCurrencyId",
                table: "MoneyExchangeOperations");

            migrationBuilder.DropIndex(
                name: "IX_MoneyExchangeOperations_RateBaseCurrencyId",
                table: "MoneyExchangeOperations");

            migrationBuilder.DropIndex(
                name: "IX_MoneyExchangeOperations_RateQuoteCurrencyId",
                table: "MoneyExchangeOperations");

            migrationBuilder.Sql("""
                DELETE FROM [Accounts]
                WHERE [AccountCode] IN (N'1201', N'2101')
                  AND NOT EXISTS (
                      SELECT 1 FROM [LedgerEntries] le WHERE le.[AccountId] = [Accounts].[Id]
                  );
                """);

            migrationBuilder.DropColumn(
                name: "ExchangeProfitAmount",
                table: "MoneyExchangeOperations");

            migrationBuilder.DropColumn(
                name: "InventoryCostDecrease",
                table: "MoneyExchangeOperations");

            migrationBuilder.DropColumn(
                name: "InventoryCostIncrease",
                table: "MoneyExchangeOperations");

            migrationBuilder.DropColumn(
                name: "RateBaseCurrencyId",
                table: "MoneyExchangeOperations");

            migrationBuilder.DropColumn(
                name: "RateQuoteCurrencyId",
                table: "MoneyExchangeOperations");

            migrationBuilder.DropColumn(
                name: "ShortLiabilityDecrease",
                table: "MoneyExchangeOperations");

            migrationBuilder.DropColumn(
                name: "ShortLiabilityIncrease",
                table: "MoneyExchangeOperations");

            migrationBuilder.DropColumn(
                name: "QuotationPriority",
                table: "Currencies");
        }
    }
}
