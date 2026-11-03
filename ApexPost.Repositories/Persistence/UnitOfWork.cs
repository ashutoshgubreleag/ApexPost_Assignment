using ApexPost.Database;
using ApexPost.Models.Exceptions;
using ApexPost.Services.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Repositories.Persistence;

/// <summary>
/// EF Core unit of work. It shares the request's scoped <see cref="ApexPostDbContext"/> with every
/// repository, so whatever they staged is committed by ONE SaveChangesAsync (one SQL transaction).
/// Provider exceptions are translated into application exceptions here, so nothing above this
/// layer ever references EF Core or SqlClient.
/// </summary>
public sealed class UnitOfWork(ApexPostDbContext db) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(
                "Another counter changed this record at the same time (for example, sold the last unit). Your change was not saved; please check the latest figures and try again.",
                ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 } sql)
        {
            // 2627 = UNIQUE/PRIMARY KEY constraint, 2601 = unique index. Only the constraint name
            // travels upwards; the duplicate value (e.g. an e-mail) stays in the server log.
            throw new DuplicateKeyException(SqlErrorText.ConstraintName(sql.Message), ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 547 } sql && sql.Message.Contains("CHECK constraint", StringComparison.OrdinalIgnoreCase))
        {
            // Last line of defence (e.g. stock below zero): the domain should have refused first.
            throw new BusinessRuleViolationException("DB_CONSTRAINT", "The change breaks a data rule and was not saved.");
        }
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        // With EnableRetryOnFailure, a user transaction must run inside the execution strategy so the
        // whole block (not a single statement) is retried after a transient fault.
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            (Db: db, Operation: operation),
            static async (_, state, ct) =>
            {
                await using var transaction = await state.Db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
                var result = await state.Operation(ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return result;
            },
            verifySucceeded: null,
            cancellationToken).ConfigureAwait(false);
    }

    public void DiscardChanges() => db.ChangeTracker.Clear();
}

/// <summary>Reads the constraint or index name out of a SQL Server error message.</summary>
public static partial class SqlErrorText
{
    public static string ConstraintName(string message)
    {
        // 2627: "Violation of UNIQUE KEY constraint 'UQ_Staff_Email'. Cannot insert duplicate key ..."
        // 2601: "Cannot insert duplicate key row in object 'dbo.Staff' with unique index 'IX_...'. ..."
        var match = NamePattern().Match(message ?? string.Empty);
        return match.Success ? match.Groups["name"].Value : "unique key";
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"(?:constraint|unique index) '(?<name>[^']+)'", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex NamePattern();
}
