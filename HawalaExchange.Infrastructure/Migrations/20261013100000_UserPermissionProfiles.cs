using HawalaExchange.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HawalaExchange.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261013100000_UserPermissionProfiles")]
public sealed class UserPermissionProfiles:Migration
{
    protected override void Up(MigrationBuilder builder)=>builder.Sql("""
        CREATE TABLE dbo.UserPermissionProfiles(
            TenantId bigint NOT NULL,UserId bigint NOT NULL,PermissionsJson nvarchar(max) NOT NULL,
            ModifiedAt datetime2 NOT NULL,ModifiedBy bigint NOT NULL,
            CONSTRAINT PK_UserPermissionProfiles PRIMARY KEY(TenantId,UserId),
            CONSTRAINT CK_UserPermissionProfiles_Json CHECK(ISJSON(PermissionsJson)=1));
        """);
    protected override void Down(MigrationBuilder builder)=>builder.Sql("DROP TABLE dbo.UserPermissionProfiles;");
}
