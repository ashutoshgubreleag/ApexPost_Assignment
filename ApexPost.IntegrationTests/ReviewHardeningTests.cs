using ApexPost.Database;
using ApexPost.Database.Sql;
using ApexPost.Models.Domain;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Abstractions;
using ApexPost.Tests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace ApexPost.IntegrationTests;

/// <summary>
/// Week 3 review fixes on real SQL Server: unique-key violations surface as DuplicateKeyException
/// (HTTP 409) with only the constraint name; the app role grants per table, not per schema;
/// usp_AdjustStock checks XACT_STATE(); the HardenAppRoleAndAdjustStock migration goes down and up.
/// </summary>
public sealed class ReviewHardeningTests
{
    private const string PreviousMigration = "20261031075824_FixInactiveStaffSeed";

    [Fact]
    public async Task Unique_key_violation_becomes_DuplicateKeyException_without_the_duplicate_value()
    {
        await using var harness = await ServiceHarness.CreateAsync();
        using var scope = harness.NewScope();
        var staff = scope.ServiceProvider.GetRequiredService<IStaffRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Bypasses the service's "e-mail already used" check, like two requests racing.
        staff.Add(StaffMember.Create("Duplicate Example", "clerk.br001a@apexpost.local", StaffRole.CounterClerk, new BranchCode("BR-001"), "PBKDF2$1$c2FsdA==$aGFzaA=="));

        var ex = await Assert.ThrowsAsync<DuplicateKeyException>(() => unitOfWork.SaveChangesAsync(CancellationToken.None));
        Assert.Equal("UQ_Staff_Email", ex.ConstraintName);
        Assert.DoesNotContain("clerk.br001a", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task App_role_has_no_schema_wide_grant()
    {
        await using var database = await SqlTestDatabase.CreateAsync();

        var schemaGrants = await ScalarAsync(database, """
            SELECT COUNT(*)
            FROM sys.database_permissions AS p
            JOIN sys.database_principals AS r ON r.principal_id = p.grantee_principal_id
            WHERE r.name = N'apexpost_app' AND p.class_desc = N'SCHEMA' AND p.state IN ('G', 'W')
            """);

        Assert.Equal(0, schemaGrants);
    }

    [Fact]
    public async Task App_role_has_exactly_the_per_table_permissions()
    {
        (string Securable, string Permission, bool Expected)[] matrix =
        [
            ("dbo.Roles", "SELECT", true),
            ("dbo.Roles", "INSERT", false),
            ("dbo.ShippingRates", "UPDATE", false),
            ("dbo.Products", "UPDATE", true),
            ("dbo.Products", "DELETE", false),
            ("dbo.CounterTransactions", "UPDATE", true),
            ("dbo.CounterTransactions", "DELETE", false),
            ("dbo.TransactionLines", "DELETE", true),
            ("dbo.Payments", "INSERT", true),
            ("dbo.Payments", "UPDATE", false),
            ("dbo.Payments", "DELETE", false),
            ("dbo.StockAdjustments", "SELECT", true),
            ("dbo.StockAdjustments", "INSERT", false),
            ("dbo.AuditEntries", "INSERT", true),
            ("dbo.AuditEntries", "UPDATE", false),
            ("dbo.SyncInbox", "UPDATE", false),
            ("dbo.usp_AdjustStock", "EXECUTE", true),
            ("dbo.__EFMigrationsHistory", "SELECT", false),
        ];
        await using var database = await SqlTestDatabase.CreateAsync();
        await ScalarAsync(database, """
            CREATE USER [apexpost_role_probe] WITHOUT LOGIN;
            ALTER ROLE apexpost_app ADD MEMBER [apexpost_role_probe];
            SELECT 1;
            """);

        var wrong = new List<string>();
        foreach (var (securable, permission, expected) in matrix)
        {
            // A user with no login, member of the role only, asks "may I?" as itself.
            var allowed = await ScalarAsync(database, """
                EXECUTE AS USER = N'apexpost_role_probe';
                DECLARE @allowed int = HAS_PERMS_BY_NAME(@securable, N'OBJECT', @permission);
                REVERT;
                SELECT @allowed;
                """,
                new SqlParameter("@securable", securable),
                new SqlParameter("@permission", permission));

            if (allowed != (expected ? 1 : 0))
            {
                wrong.Add($"{permission} on {securable}: expected {(expected ? "allowed" : "denied")}");
            }
        }

        Assert.Empty(wrong);
    }

    [Fact]
    public async Task Adjust_stock_procedure_checks_XACT_STATE()
    {
        await using var database = await SqlTestDatabase.CreateAsync();

        var usesXactState = await ScalarAsync(database,
            "SELECT CASE WHEN OBJECT_DEFINITION(OBJECT_ID(N'dbo.usp_AdjustStock')) LIKE N'%IF XACT_STATE() <> 0%' THEN 1 ELSE 0 END");

        Assert.Equal(1, usesXactState);
    }

    [Fact]
    public async Task Hardening_migration_goes_down_to_version_1_and_back_up()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var db = database.NewContext();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(PreviousMigration);
        Assert.Equal(1, await SchemaGrantCountAsync(database));
        Assert.Equal(0, await ScalarAsync(database, "SELECT CASE WHEN OBJECT_DEFINITION(OBJECT_ID(N'dbo.usp_AdjustStock')) LIKE N'%XACT_STATE()%' THEN 1 ELSE 0 END"));

        await migrator.MigrateAsync();
        Assert.Equal(0, await SchemaGrantCountAsync(database));
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public void Version_1_scripts_are_kept_for_the_old_migration()
    {
        Assert.Contains("@@TRANCOUNT", SqlScripts.Read(SqlScripts.AdjustStockV1), StringComparison.Ordinal);
        Assert.DoesNotContain("XACT_STATE", SqlScripts.Read(SqlScripts.AdjustStockV1), StringComparison.Ordinal);
        Assert.Contains("ON SCHEMA::dbo TO apexpost_app", SqlScripts.Read(SqlScripts.AppRoleV1), StringComparison.Ordinal);
        Assert.Contains("REVOKE SELECT, INSERT, UPDATE ON SCHEMA::dbo", SqlScripts.Read(SqlScripts.AppRole), StringComparison.Ordinal);
    }

    private static Task<int> SchemaGrantCountAsync(SqlTestDatabase database) => ScalarAsync(database, """
        SELECT COUNT(DISTINCT p.class)
        FROM sys.database_permissions AS p
        JOIN sys.database_principals AS r ON r.principal_id = p.grantee_principal_id
        WHERE r.name = N'apexpost_app' AND p.class_desc = N'SCHEMA' AND p.state IN ('G', 'W')
        """);

    private static async Task<int> ScalarAsync(SqlTestDatabase database, string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
