using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace HawalaExchange.Infrastructure.Migrations;
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261103160000_AgentPaymentBills")]
public sealed class AgentPaymentBills : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<bool>("RequiresManualExecution","Correspondents",type:"bit",nullable:false,defaultValue:false);
        m.Sql("""
            CREATE TABLE dbo.AgentPaymentBills(
                Id bigint IDENTITY PRIMARY KEY,TenantId bigint NOT NULL,CorrespondentId bigint NOT NULL,
                Status nvarchar(20) NOT NULL,CreatedAt datetime2 NOT NULL,CreatedBy bigint NOT NULL,
                PaidAt datetime2 NULL,PaidBy bigint NULL,CancelledAt datetime2 NULL,CancelledBy bigint NULL,
                SnapshotJson nvarchar(max) NOT NULL,RowVersion rowversion NOT NULL,
                CONSTRAINT AK_AgentPaymentBills_TenantId_Id UNIQUE(TenantId,Id),
                CONSTRAINT FK_AgentPaymentBills_Correspondents_TenantId_CorrespondentId FOREIGN KEY(TenantId,CorrespondentId) REFERENCES dbo.Correspondents(TenantId,Id));
            CREATE INDEX IX_AgentPaymentBills_TenantId_CorrespondentId_CreatedAt ON dbo.AgentPaymentBills(TenantId,CorrespondentId,CreatedAt);
            CREATE TABLE dbo.AgentPaymentBillItems(
                Id bigint IDENTITY PRIMARY KEY,TenantId bigint NOT NULL,BillId bigint NOT NULL,HawalaId bigint NOT NULL,IsActive bit NOT NULL,
                CONSTRAINT AK_AgentPaymentBillItems_TenantId_Id UNIQUE(TenantId,Id),
                CONSTRAINT FK_AgentPaymentBillItems_AgentPaymentBills_TenantId_BillId FOREIGN KEY(TenantId,BillId) REFERENCES dbo.AgentPaymentBills(TenantId,Id),
                CONSTRAINT FK_AgentPaymentBillItems_Hawalas_TenantId_HawalaId FOREIGN KEY(TenantId,HawalaId) REFERENCES dbo.Hawalas(TenantId,Id));
            CREATE INDEX IX_AgentPaymentBillItems_TenantId_BillId ON dbo.AgentPaymentBillItems(TenantId,BillId);
            CREATE UNIQUE INDEX IX_AgentPaymentBillItems_TenantId_HawalaId ON dbo.AgentPaymentBillItems(TenantId,HawalaId) WHERE IsActive=1;
            """);
    }
    protected override void Down(MigrationBuilder m)
    {
        m.DropTable("AgentPaymentBillItems"); m.DropTable("AgentPaymentBills");
        m.DropColumn("RequiresManualExecution","Correspondents");
    }
}
