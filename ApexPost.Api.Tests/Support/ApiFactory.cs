using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net.Http.Headers;
using System.Text;
using ApexPost.Api.Security;
using ApexPost.Services.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ApexPost.Api.Tests.Support;

/// <summary>
/// Hosts the real ApexPost.Api pipeline (routing, model binding, validation, exception handler) in
/// memory. Only the three persistence ports are replaced, with <see cref="InMemoryTrackingStore"/>, so no SQL Server is needed.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Test-only signing key (fictional; never used outside these tests).</summary>
    public const string TestSigningKey = "test-only-signing-key-not-a-secret-0123456789";

    /// <summary>Test issuer, audience and partner key.</summary>
    public const string TestIssuer = "https://apexpost.test/issuer";

    /// <inheritdoc cref="TestIssuer"/>
    public const string TestAudience = "apexpost-api-tests";

    /// <inheritdoc cref="TestIssuer"/>
    public const string TestPartnerKey = "test-partner-key-fictional-0001";

    /// <summary>The "database" behind this host.</summary>
    public InMemoryTrackingStore Store { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ApexPostDb"] = "Server=unused-in-api-tests",
            ["Jwt:Issuer"] = TestIssuer,
            ["Jwt:Audience"] = TestAudience,
            ["Jwt:SigningKey"] = TestSigningKey,
            ["Partner:ApiKeys:0"] = TestPartnerKey,
            ["Partner:ApiKeys:1"] = "test-partner-key-fictional-0002",
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITrackedShipmentRepository>();
            services.RemoveAll<ITrackingQueries>();
            services.RemoveAll<IUnitOfWork>();
            services.AddSingleton(Store.Repository);
            services.AddSingleton(Store.Queries);
            services.AddSingleton(Store.UnitOfWork);
        });
    }
}

/// <summary>Token and client helpers for tests.</summary>
public static class AuthTestExtensions
{
    /// <summary>Issues a valid token through the API's own token service.</summary>
    public static string CreateToken(this ApiFactory factory, string role, string courierId = "C-1001")
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory.Services.GetRequiredService<ITokenService>().Issue(courierId, role).AccessToken;
    }

    /// <summary>Sets <c>Authorization: Bearer</c> on the client and returns it.</summary>
    public static HttpClient WithToken(this HttpClient client, string token)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>A client signed in as a courier (or another role) of the given factory.</summary>
    public static HttpClient SignedIn(this HttpClient client, ApiFactory factory, string role = ApiRoles.Courier, string courierId = "C-1001") =>
        client.WithToken(factory.CreateToken(role, courierId));

    /// <summary>Builds a token with the test key but a custom lifetime/issuer/key, to test rejection of bad tokens.</summary>
    public static string CreateCustomToken(string role, DateTime notBefore, DateTime expires, string signingKey = ApiFactory.TestSigningKey, string issuer = ApiFactory.TestIssuer, string audience = ApiFactory.TestAudience) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            NotBefore = notBefore,
            Expires = expires,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = "C-1001",
                ["courierId"] = "C-1001",
                ["role"] = role,
            },
        });
}

/// <summary>A query service that always fails, to prove unexpected errors come back as a safe 500.</summary>
public sealed class ThrowingQueries : ITrackingQueries
{
    public const string SecretMessage = "Server=prod-db;Password=hunter2";

    public Task<ApexPost.Services.Tracking.PagedResult<ApexPost.Services.Tracking.TrackedShipmentDto>> ListAsync(ApexPost.Services.Tracking.TrackingListQuery query, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(SecretMessage);

    public Task<ApexPost.Services.Tracking.TrackedShipmentDto?> GetAsync(string trackingNumber, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(SecretMessage);

    public Task<IReadOnlyList<ApexPost.Services.Tracking.ScanDto>?> GetScansAsync(string trackingNumber, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(SecretMessage);
}
