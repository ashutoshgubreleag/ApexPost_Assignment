using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ApexPost.Database;

/// <summary>
/// Lets the dotnet-ef CLI build the model without starting the web app:
///   dotnet ef migrations add &lt;Name&gt; --project ApexPost.Database
///   dotnet ef migrations script --idempotent --project ApexPost.Database -o database/schema.sql
/// The connection string is only used by "database update"; set APEXPOST_DESIGN_CONNECTION to
/// target another server. No credentials are stored here (Windows authentication).
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApexPostDbContext>
{
    public ApexPostDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("APEXPOST_DESIGN_CONNECTION")
            ?? "Server=.;Database=ApexPostDb;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<ApexPostDbContext>()
            .UseSqlServer(connection, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "dbo"))
            .Options;
        return new ApexPostDbContext(options);
    }
}
