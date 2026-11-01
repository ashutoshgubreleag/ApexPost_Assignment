using System.Data.Common;
using ApexPost.Database;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ApexPost.Tests.Support;

/// <summary>
/// A throw-away SQL Server database built with the real migrations (never EnsureCreated), so
/// every test sees the production schema: constraints, seed data, indexes and stored procedures.
/// </summary>
public sealed class SqlTestDatabase : IAsyncDisposable
{
    public const string ServerVariable = "APEXPOST_TEST_SQL";
    private const string DefaultServer = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=True;TrustServerCertificate=True";

    private SqlTestDatabase(string name, string connectionString)
    {
        Name = name;
        ConnectionString = connectionString;
    }

    public string Name { get; }

    public string ConnectionString { get; }

    public static async Task<SqlTestDatabase> CreateAsync()
    {
        var server = Environment.GetEnvironmentVariable(ServerVariable);
        var builder = new SqlConnectionStringBuilder(string.IsNullOrWhiteSpace(server) ? DefaultServer : server)
        {
            InitialCatalog = "ApexPostTest_" + Guid.NewGuid().ToString("N")[..12],
            MultipleActiveResultSets = false,
        };

        var database = new SqlTestDatabase(builder.InitialCatalog, builder.ConnectionString);
        await using var context = database.NewContext();
        await context.Database.MigrateAsync().ConfigureAwait(false);
        return database;
    }

    /// <summary>A context on this database, optionally with extra interceptors (command counting, SQL capture).</summary>
    public ApexPostDbContext NewContext(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<ApexPostDbContext>()
            .UseSqlServer(ConnectionString, sql => sql.MigrationsAssembly(typeof(ApexPostDbContext).Assembly.GetName().Name))
            .AddInterceptors(interceptors)
            .Options;
        return new ApexPostDbContext(options);
    }

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        var master = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" }.ConnectionString;
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        // The name is passed as a parameter and quoted by the server (QUOTENAME), not concatenated here.
        command.CommandText = """
            IF DB_ID(@name) IS NOT NULL
            BEGIN
                DECLARE @sql nvarchar(max) = N'ALTER DATABASE ' + QUOTENAME(@name) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' + QUOTENAME(@name) + N';';
                EXEC sys.sp_executesql @sql;
            END
            """;
        command.Parameters.Add(new SqlParameter("@name", System.Data.SqlDbType.NVarChar, 128) { Value = Name });
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}

/// <summary>Counts and records the SQL commands a piece of code sends (N+1 detection, ToQueryString evidence).</summary>
public sealed class CommandRecorder : DbCommandInterceptor
{
    private readonly List<string> _commands = [];

    public IReadOnlyList<string> Commands => _commands;

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _commands.Add(command.CommandText);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public void Clear() => _commands.Clear();
}
