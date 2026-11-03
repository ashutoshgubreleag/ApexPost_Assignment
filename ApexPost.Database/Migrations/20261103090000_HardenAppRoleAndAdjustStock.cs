using ApexPost.Database.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexPost.Database.Migrations
{
    /// <summary>
    /// Week 3 review fixes, no model change:
    /// <list type="bullet">
    /// <item><c>usp_AdjustStock</c> version 2: the CATCH block checks <c>XACT_STATE()</c>
    /// (doomed / committable / none) instead of only <c>@@TRANCOUNT</c>.</item>
    /// <item><c>apexpost_app</c> version 2: the schema-wide SELECT/INSERT/UPDATE grant is revoked
    /// and replaced by per-table grants that match what the application does.</item>
    /// </list>
    /// Down restores both version-1 scripts from Sql/History.
    /// </summary>
    public partial class HardenAppRoleAndAdjustStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(SqlScripts.ReadAsDeferredBatch(SqlScripts.AdjustStock));
            migrationBuilder.Sql(SqlScripts.Read(SqlScripts.AppRole));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(SqlScripts.Read(SqlScripts.AppRoleV2Revoke));
            migrationBuilder.Sql(SqlScripts.Read(SqlScripts.AppRoleV1));
            migrationBuilder.Sql(SqlScripts.ReadAsDeferredBatch(SqlScripts.AdjustStockV1));
        }
    }
}
