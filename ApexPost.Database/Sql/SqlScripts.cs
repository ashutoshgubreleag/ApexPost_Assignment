namespace ApexPost.Database.Sql;

/// <summary>
/// Reads the embedded T-SQL files deployed by migrations and tests:
/// <list type="bullet">
/// <item><c>Sql/Procedures</c>, <c>Sql/Security</c>: the current version of each object.</item>
/// <item><c>Sql/History</c>: earlier versions, byte for byte as first deployed, so an old migration
/// always deploys exactly what it deployed when it was written (migrations are history and must not
/// change behaviour).</item>
/// </list>
/// </summary>
public static class SqlScripts
{
    /// <summary>Procedures as deployed by 20261030124158_AddStoredProcedures (version 1).</summary>
    public static readonly IReadOnlyList<string> InitialProcedures = ["usp_DailyBranchSales", "usp_LowStockReport", AdjustStockV1];

    /// <summary>Current version of usp_AdjustStock (CATCH checks XACT_STATE()).</summary>
    public const string AdjustStock = "usp_AdjustStock";

    /// <summary>usp_AdjustStock version 1 (CATCH checked @@TRANCOUNT only).</summary>
    public const string AdjustStockV1 = "usp_AdjustStock.v1";

    /// <summary>Current app role: per-table grants.</summary>
    public const string AppRole = "AppRole";

    /// <summary>App role version 1: schema-wide SELECT/INSERT/UPDATE plus DENYs.</summary>
    public const string AppRoleV1 = "AppRole.v1";

    /// <summary>Removes the per-table grants of version 2 (Down of HardenAppRoleAndAdjustStock).</summary>
    public const string AppRoleV2Revoke = "AppRole.v2-revoke";

    /// <summary>
    /// A CREATE OR ALTER PROCEDURE must be the first statement of its batch, but the idempotent
    /// deployment script wraps every migration step in IF NOT EXISTS (...) BEGIN ... END. Running the
    /// reviewed, static script through EXEC keeps it valid in both modes. No runtime value is ever
    /// placed in this text; it is the embedded file, with quotes escaped for the N'' literal.
    /// </summary>
    public static string ReadAsDeferredBatch(string name) =>
        "EXEC(N'" + Read(name).Replace("'", "''", StringComparison.Ordinal) + "');";

    public static string Read(string name)
    {
        var resource = $"ApexPost.Sql.{name}.sql";
        using var stream = typeof(SqlScripts).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded SQL script {resource} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
