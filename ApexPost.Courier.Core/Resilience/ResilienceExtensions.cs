using ApexPost.Courier.Core.Api;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace ApexPost.Courier.Core.Resilience;

public static class ResilienceExtensions
{
    /// <summary>
    /// Pipeline, outermost first: total timeout -> retry (exponential backoff + jitter) -> circuit breaker -> per-attempt timeout.
    /// Only transient failures are handled: no connection (HttpRequestException), an attempt timeout, 408 and 5xx.
    /// 400/401/403/404/409 are the server's real answer; repeating them would be pointless, so they pass straight through.
    /// The circuit sits inside the retry so every failed attempt counts toward opening it; once open, calls fail instantly.
    /// POST is retried because the upload is idempotent (the server de-duplicates on ScanId).
    /// </summary>
    public static IHttpClientBuilder AddApexResilience(this IHttpClientBuilder builder, ResilienceSettings settings, CircuitStateMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(monitor);

        builder.AddResilienceHandler("apex-api", pipeline =>
        {
            pipeline.AddTimeout(settings.TotalTimeout);

            pipeline.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = settings.MaxRetryAttempts,
                Delay = settings.RetryBaseDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = static args => new ValueTask<bool>(IsTransient(args.Outcome)),
            });

            pipeline.AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                FailureRatio = settings.CircuitFailureRatio,
                MinimumThroughput = settings.CircuitMinimumThroughput,
                SamplingDuration = settings.CircuitSamplingDuration,
                BreakDuration = settings.CircuitBreakDuration,
                ShouldHandle = static args => new ValueTask<bool>(IsTransient(args.Outcome)),
                OnOpened = _ =>
                {
                    monitor.Set(CircuitStateKind.Open);
                    return default;
                },
                OnHalfOpened = _ =>
                {
                    monitor.Set(CircuitStateKind.HalfOpen);
                    return default;
                },
                OnClosed = _ =>
                {
                    monitor.Set(CircuitStateKind.Closed);
                    return default;
                },
            });

            pipeline.AddTimeout(settings.AttemptTimeout);
        });

        return builder;
    }

    private static bool IsTransient(Outcome<HttpResponseMessage> outcome) =>
        outcome.Exception is HttpRequestException or TimeoutRejectedException
        || (outcome.Result is { } r && ((int)r.StatusCode >= 500 || r.StatusCode == System.Net.HttpStatusCode.RequestTimeout));
}
