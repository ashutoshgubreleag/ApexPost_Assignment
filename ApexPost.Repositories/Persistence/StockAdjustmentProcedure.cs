using ApexPost.Database;
using ApexPost.Models.Exceptions;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Repositories.Persistence;

/// <summary>
/// Calls dbo.usp_AdjustStock. The interpolated string is turned into SqlParameters by EF Core
/// (SqlQuery takes a FormattableString), so no value is ever concatenated into the SQL text.
/// </summary>
public sealed class StockAdjustmentProcedure(ApexPostDbContext db) : IStockAdjustmentProcedure
{
    public async Task<int> AdjustAsync(BranchCode branch, Sku sku, int delta, string reason, string changedBy, CancellationToken cancellationToken)
    {
        try
        {
            var result = await db.Database
                .SqlQuery<AdjustStockResult>($"EXEC dbo.usp_AdjustStock @BranchCode = {branch.Value}, @Sku = {sku.Value}, @Delta = {delta}, @Reason = {reason}, @ChangedBy = {changedBy}")
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            return result.Single().QuantityAfter;
        }
        catch (SqlException ex) when (ex.Number == ProcedureErrors.InsufficientStock)
        {
            throw new BusinessRuleViolationException("STOCK_INSUFFICIENT", ex.Message);
        }
        catch (SqlException ex) when (ex.Number is ProcedureErrors.InvalidArgument or ProcedureErrors.NotFound)
        {
            throw new CommandValidationException("QuantityChange", ex.Message);
        }
    }

    private sealed record AdjustStockResult(int QuantityAfter, int StockAdjustmentId);
}

/// <summary>Error numbers raised with THROW by the stored procedures (database/procedures).</summary>
public static class ProcedureErrors
{
    public const int InvalidArgument = 50001;
    public const int NotFound = 50002;
    public const int InsufficientStock = 50003;
}
