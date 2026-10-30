namespace ApexPost.Database.Sql;

/// <summary>Reads the embedded T-SQL files (Sql/Procedures, Sql/Security) deployed by migrations and tests.</summary>
public static class SqlScripts
{
    public static readonly IReadOnlyList<string> Procedures = ["usp_DailyBranchSales", "usp_LowStockReport", "usp_AdjustStock"];

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
