using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace HawalaExchange.Infrastructure.Migrations;
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261104100000_LinkHawalaCurrencyExchanges")]
public sealed class LinkHawalaCurrencyExchanges : Migration
{
    protected override void Up(MigrationBuilder m) {
        m.AddColumn<long>("LinkedHawalaId","MoneyExchangeOperations",type:"bigint",nullable:true);
        m.CreateIndex("IX_MoneyExchangeOperations_TenantId_LinkedHawalaId","MoneyExchangeOperations",new[]{"TenantId","LinkedHawalaId"},unique:true,filter:"[LinkedHawalaId] IS NOT NULL AND [IsDeleted] = 0");
        m.AddForeignKey("FK_MoneyExchangeOperations_Hawalas_TenantId_LinkedHawalaId","MoneyExchangeOperations",new[]{"TenantId","LinkedHawalaId"},"Hawalas",principalColumns:new[]{"TenantId","Id"},onDelete:ReferentialAction.Restrict);
    }
    protected override void Down(MigrationBuilder m) {
        m.DropForeignKey("FK_MoneyExchangeOperations_Hawalas_TenantId_LinkedHawalaId","MoneyExchangeOperations");
        m.DropIndex("IX_MoneyExchangeOperations_TenantId_LinkedHawalaId","MoneyExchangeOperations");
        m.DropColumn("LinkedHawalaId","MoneyExchangeOperations");
    }
}
