using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HawalaExchange.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeedInitialData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Accounts",
                columns: new[] { "Id", "AccountCode", "AccountName", "AccountType", "CreatedAt", "IsActive", "ReferenceId", "ReferenceType" },
                values: new object[,]
                {
                    { 1L, "1001", "Cash", "Cash", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, null, null },
                    { 2L, "1101", "Bank", "Bank", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, null, null },
                    { 3L, "3001", "Hawala Commission Income", "Income", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, null, null },
                    { 4L, "3002", "Exchange Income", "Income", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, null, null },
                    { 5L, "4001", "Office Expense", "Expense", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, null, null },
                    { 6L, "5001", "Owner Capital", "Equity", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, null, null }
                });

            migrationBuilder.InsertData(
                table: "Branches",
                columns: new[] { "Id", "Address", "Code", "CreatedAt", "Name", "PhoneNumber" },
                values: new object[] { 1L, " ", "MAIN", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Main Branch", "null" });

            migrationBuilder.InsertData(
                table: "Currencies",
                columns: new[] { "Id", "Code", "DecimalPlaces", "IsActive", "Name", "Symbol" },
                values: new object[,]
                {
                    { 1L, "AFN", 2, true, "Afghani", "؋" },
                    { 2L, "USD", 2, true, "US Dollar", "$" },
                    { 3L, "EUR", 2, true, "Euro", "€" },
                    { 4L, "AED", 2, true, "UAE Dirham", "د.إ" },
                    { 5L, "IRR", 2, true, "Iranian Rial", "﷼" },
                    { 6L, "PKR", 2, true, "Pakistani Rupee", "₨" }
                });

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "BranchId", "CreatedAt", "FullName", "IsActive", "PasswordHash", "Role", "UserName" },
                values: new object[] { 1L, 1L, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "System Admin", true, "CHANGE_THIS_PASSWORD_HASH", "Admin", "admin" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 1L);

            migrationBuilder.DeleteData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 2L);

            migrationBuilder.DeleteData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 3L);

            migrationBuilder.DeleteData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 4L);

            migrationBuilder.DeleteData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 5L);

            migrationBuilder.DeleteData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 6L);

            migrationBuilder.DeleteData(
                table: "Currencies",
                keyColumn: "Id",
                keyValue: 1L);

            migrationBuilder.DeleteData(
                table: "Currencies",
                keyColumn: "Id",
                keyValue: 2L);

            migrationBuilder.DeleteData(
                table: "Currencies",
                keyColumn: "Id",
                keyValue: 3L);

            migrationBuilder.DeleteData(
                table: "Currencies",
                keyColumn: "Id",
                keyValue: 4L);

            migrationBuilder.DeleteData(
                table: "Currencies",
                keyColumn: "Id",
                keyValue: 5L);

            migrationBuilder.DeleteData(
                table: "Currencies",
                keyColumn: "Id",
                keyValue: 6L);

            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1L);

            migrationBuilder.DeleteData(
                table: "Branches",
                keyColumn: "Id",
                keyValue: 1L);
        }
    }
}
