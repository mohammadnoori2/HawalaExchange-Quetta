using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HawalaExchange.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHawalaStatisticsProcedure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE PROCEDURE [dbo].[usp_GetHawalaStatistics_v1]
                    @TenantId bigint
                AS
                BEGIN
                    SET NOCOUNT ON;

                    SELECT
                        COALESCE(SUM(CASE WHEN [HawalaType] = N'HawalaSend' THEN CAST(1 AS bigint) ELSE 0 END), 0) AS [HawalaSendCount],
                        COALESCE(SUM(CASE WHEN [HawalaType] = N'HawalaReceive' THEN CAST(1 AS bigint) ELSE 0 END), 0) AS [HawalaReceiveCount],
                        COALESCE(SUM(CASE WHEN [HawalaType] = N'HawalaOther' THEN CAST(1 AS bigint) ELSE 0 END), 0) AS [HawalaOtherCount],
                        COALESCE(SUM(CASE WHEN [Status] = N'Pending' THEN CAST(1 AS bigint) ELSE 0 END), 0) AS [PendingCount],
                        COALESCE(SUM(CASE WHEN [Status] = N'Paid' THEN CAST(1 AS bigint) ELSE 0 END), 0) AS [PaidCount]
                    FROM [dbo].[Hawalas]
                    WHERE [TenantId] = @TenantId;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS [dbo].[usp_GetHawalaStatistics_v1];");
        }
    }
}
