using ApexPost.Database.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexPost.Database.Migrations
{
    /// <summary>
    /// Deploys the stored procedures (CREATE OR ALTER, so re-running is safe) and the
    /// least-privilege application role. The T-SQL lives in Sql/*.sql, reviewed like any code.
    /// Uses the version-1 scripts (Sql/History) so it keeps deploying exactly what it deployed
    /// originally; 20261103090000_HardenAppRoleAndAdjustStock upgrades both objects.
    /// </summary>
    public partial class AddStoredProcedures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var procedure in SqlScripts.InitialProcedures)
            {
                migrationBuilder.Sql(SqlScripts.ReadAsDeferredBatch(procedure));
            }

            migrationBuilder.Sql(SqlScripts.Read(SqlScripts.AppRoleV1));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF DATABASE_PRINCIPAL_ID(N'apexpost_app') IS NOT NULL DROP ROLE apexpost_app;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_AdjustStock;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_LowStockReport;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_DailyBranchSales;");
        }
    }
}
