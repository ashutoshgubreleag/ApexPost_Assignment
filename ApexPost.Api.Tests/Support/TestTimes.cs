namespace ApexPost.Api.Tests.Support;

/// <summary>
/// Fixed instants for tests, so no test reads the wall clock (Week 5 determinism rule).
/// The API under test still validates JWT lifetimes and "scan in the future" against its own clock, so the values are
/// chosen to be unambiguous on any run: <see cref="PastReference"/> is safely in the past, <see cref="FarFuture"/> and
/// <see cref="FarPast"/> are decades away from any real clock.
/// </summary>
public static class TestTimes
{
    /// <summary>A fixed moment in the past used as "when the courier scanned".</summary>
    public static readonly DateTimeOffset PastReference = new(2026, 1, 15, 8, 0, 0, TimeSpan.Zero);

    /// <summary>A moment that is always in the future for the server (scan in the future, token not yet expired).</summary>
    public static readonly DateTimeOffset FarFuture = new(2999, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A moment that is always in the past for the server (expired token).</summary>
    public static readonly DateTimeOffset FarPast = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
