#if HAS_GENERATED_CLIENT
using Microsoft.Extensions.DependencyInjection;

namespace ApexPost.Client;

/// <summary>Hand-written wrapper around the generated <see cref="ApexPostClient"/>: dependency-injection registration only.</summary>
public static class ApexPostClientServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IApexPostClient"/> as a typed HttpClient pointing at the API. The generated client
    /// takes its base address from the HttpClient, so one registration works for local, staging and production.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="baseAddress">API root, for example <c>https://localhost:7153/</c>.</param>
    /// <returns>The HttpClient builder, so callers can add handlers (authentication, resilience).</returns>
    public static IHttpClientBuilder AddApexPostClient(this IServiceCollection services, Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(baseAddress);
        return services.AddHttpClient<IApexPostClient, ApexPostClient>(http => http.BaseAddress = baseAddress);
    }
}
#endif
