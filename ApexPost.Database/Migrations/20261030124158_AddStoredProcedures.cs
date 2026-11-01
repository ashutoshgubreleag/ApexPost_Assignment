using ApexPost.Database.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexPost.Database.Migrations
{
    /// <summary>
    /// Deploys the stored procedures (CREATE OR ALTER, so re-running is safe) and the
    /// least-privilege application role. The T-SQL lives in Sql/*.sql, reviewed like any code.
    /// </summary>
    public partial class AddStoredProcedures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var procedure in SqlScripts.Procedures)
            {
                migrationBuilder.Sql(SqlScripts.ReadAsDeferredBatch(procedure));
            }

            migrationBuilder.Sql(SqlScripts.Read("AppRole"));
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
