using ApexPost.Models.Exceptions;
using ApexPost.Services.Abstractions;

namespace ApexPost.Services.Common;

/// <summary>
/// Optimistic concurrency policy for write use cases. When the unit of work reports a rowversion
/// conflict (another clerk changed the same stock row or basket first), the staged changes are
/// discarded and the whole use case runs again on fresh data, so the business rules are re-checked
/// against the new state ("only 0 left" becomes a normal STOCK_INSUFFICIENT error). After the
/// configured number of attempts the conflict is surfaced to the user as HTTP 409.
/// </summary>
public static class ConcurrencyRetry
{
    public static async Task<T> ExecuteAsync<T>(IUnitOfWork unitOfWork, int attempts, Func<CancellationToken, Task<T>> useCase, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(useCase);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await useCase(cancellationToken).ConfigureAwait(false);
            }
            catch (ConcurrencyConflictException) when (attempt < attempts)
            {
                unitOfWork.DiscardChanges();

                // Small, growing, jittered pause so competing requests do not collide again in lock-step.
                var delay = TimeSpan.FromMilliseconds(Random.Shared.Next(5, 20) * attempt);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public static Task ExecuteAsync(IUnitOfWork unitOfWork, int attempts, Func<CancellationToken, Task> useCase, CancellationToken cancellationToken) =>
        ExecuteAsync(unitOfWork, attempts, async ct =>
        {
            await useCase(ct).ConfigureAwait(false);
            return true;
        }, cancellationToken);
}
