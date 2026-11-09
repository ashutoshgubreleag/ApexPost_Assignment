namespace ApexPost.Courier.Core.Resilience;

public enum CircuitStateKind
{
    Closed,
    Open,
    HalfOpen,
}

/// <summary>Lets the UI show "server unavailable, retrying" without knowing about Polly.</summary>
public interface ICircuitStateMonitor
{
    CircuitStateKind State { get; }

    event EventHandler<CircuitStateKind>? StateChanged;
}

/// <summary>Written by the circuit breaker callbacks (see <see cref="ResilienceExtensions"/>), read by the UI.</summary>
public sealed class CircuitStateMonitor : ICircuitStateMonitor
{
    private int _state;

    public CircuitStateKind State => (CircuitStateKind)Volatile.Read(ref _state);

    public event EventHandler<CircuitStateKind>? StateChanged;

    public void Set(CircuitStateKind state)
    {
        if (Interlocked.Exchange(ref _state, (int)state) != (int)state)
        {
            StateChanged?.Invoke(this, state);
        }
    }
}
