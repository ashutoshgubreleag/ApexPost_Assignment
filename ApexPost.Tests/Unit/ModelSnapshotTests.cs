using ApexPost.Database;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Tests.Unit;

/// <summary>Runs without a database: the committed migrations must describe the current model exactly.</summary>
public sealed class ModelSnapshotTests
{
    [Fact]
    public void Migrations_snapshot_matches_the_model()
    {
        var options = new DbContextOptionsBuilder<ApexPostDbContext>().UseSqlServer("Server=not-used;Database=not-used").Options;
        using var db = new ApexPostDbContext(options);

        Assert.False(db.Database.HasPendingModelChanges(), "Run: dotnet ef migrations add <Name> --project ApexPost.Database");
        Assert.Contains(db.Database.GetMigrations(), m => m.EndsWith("_AddStoredProcedures", StringComparison.Ordinal));
    }
}
