using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HawalaExchange.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260727170000_AddDefaultProfitCurrencyToCompanySettings")]
public partial class AddDefaultProfitCurrencyToCompanySettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'[dbo].[CompanySettings]', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.CompanySettings', N'DefaultProfitCurrencyId') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[CompanySettings]
                    ADD [DefaultProfitCurrencyId] bigint NULL;
                END;

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE [name] = N'IX_CompanySettings_DefaultProfitCurrencyId'
                      AND [object_id] = OBJECT_ID(N'[dbo].[CompanySettings]'))
                BEGIN
                    CREATE INDEX [IX_CompanySettings_DefaultProfitCurrencyId]
                    ON [dbo].[CompanySettings] ([DefaultProfitCurrencyId]);
                END;

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.foreign_keys
                    WHERE [name] = N'FK_CompanySettings_Currencies_DefaultProfitCurrencyId'
                      AND [parent_object_id] = OBJECT_ID(N'[dbo].[CompanySettings]'))
                BEGIN
                    ALTER TABLE [dbo].[CompanySettings] WITH CHECK
                    ADD CONSTRAINT [FK_CompanySettings_Currencies_DefaultProfitCurrencyId]
                    FOREIGN KEY ([DefaultProfitCurrencyId])
                    REFERENCES [dbo].[Currencies] ([Id]);
                END;
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'[dbo].[CompanySettings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.CompanySettings', N'DefaultProfitCurrencyId') IS NOT NULL
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM sys.foreign_keys
                    WHERE [name] = N'FK_CompanySettings_Currencies_DefaultProfitCurrencyId'
                      AND [parent_object_id] = OBJECT_ID(N'[dbo].[CompanySettings]'))
                BEGIN
                    ALTER TABLE [dbo].[CompanySettings]
                    DROP CONSTRAINT [FK_CompanySettings_Currencies_DefaultProfitCurrencyId];
                END;

                IF EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE [name] = N'IX_CompanySettings_DefaultProfitCurrencyId'
                      AND [object_id] = OBJECT_ID(N'[dbo].[CompanySettings]'))
                BEGIN
                    DROP INDEX [IX_CompanySettings_DefaultProfitCurrencyId]
                    ON [dbo].[CompanySettings];
                END;

                ALTER TABLE [dbo].[CompanySettings]
                DROP COLUMN [DefaultProfitCurrencyId];
            END;
            """);
    }
}
