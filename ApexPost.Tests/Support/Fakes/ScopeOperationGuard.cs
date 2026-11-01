using System.Reflection;

namespace ApexPost.Tests.Support.Fakes;

/// <summary>
/// Emulates EF Core's rule that a DbContext allows one operation at a time. One guard per DI scope
/// (per request), shared by every fake query service, exactly like the scoped ApexPostDbContext.
/// Without it the in-memory fakes return completed tasks and code that runs two queries of the same
/// request in parallel (Task.WhenAll) passes the tests but fails on SQL Server.
/// </summary>
public sealed class ScopeOperationGuard
{
    private int _busy;

    public async Task<T> RunAsync<T>(Func<object?> start)
    {
        ArgumentNullException.ThrowIfNull(start);
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            throw new InvalidOperationException(
                "A second operation was started on this context instance before a previous operation completed. " +
                "(Test guard emulating EF Core: do not run queries of one request concurrently.)");
        }

        try
        {
            await Task.Yield(); // behave like real I/O, so an overlapping call can actually overlap
            return await ((Task<T>)start()!).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }
}

/// <summary>Wraps a fake query service so every Task-returning call goes through the scope's guard.</summary>
public class GuardedProxy<TInterface> : DispatchProxy
    where TInterface : class
{
    private static readonly MethodInfo RunAsync = typeof(ScopeOperationGuard).GetMethod(nameof(ScopeOperationGuard.RunAsync))!;

    private object _target = null!;
    private ScopeOperationGuard _guard = null!;

    public static TInterface Create(object target, ScopeOperationGuard guard)
    {
        var proxy = Create<TInterface, GuardedProxy<TInterface>>();
        var self = (GuardedProxy<TInterface>)(object)proxy;
        self._target = target;
        self._guard = guard;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        var returnType = targetMethod.ReturnType;
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            Func<object?> start = () =>
            {
                try
                {
                    return targetMethod.Invoke(_target, args);
                }
                catch (TargetInvocationException ex) when (ex.InnerException is not null)
                {
                    throw ex.InnerException;
                }
            };
            return RunAsync.MakeGenericMethod(returnType.GetGenericArguments()[0]).Invoke(_guard, [start]);
        }

        return targetMethod.Invoke(_target, args);
    }
}
