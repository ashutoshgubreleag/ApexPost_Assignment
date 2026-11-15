using System.Security.Claims;
using System.Text.Encodings.Web;
using ApexPost.Api.Security;
using ApexPost.Tests.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApexPost.IntegrationTests.Api;

/// <summary>
/// Hosts the REAL ApexPost.Api (routing, model binding, filters, exception handler, EF Core repositories, unit of work)
/// in memory, over a disposable SQL Server database built from the real migrations. Nothing in the persistence stack is
/// replaced. Only authentication is swapped for <see cref="TestAuthHandler"/> (TESTING-STRATEGY.md §8):
/// JWT validation is already proven by ApexPost.Api.Tests/SecurityTests; here the subject is authorization + database.
/// </summary>
public sealed class SqlApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public SqlTestDatabase Database { get; private set; } = null!;

    public async Task InitializeAsync() => Database = await SqlTestDatabase.CreateAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await Database.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ApexPostDb"] = Database.ConnectionString,
            ["Database:MaxRetryCount"] = "0",
            ["Database:LogSqlCommands"] = "false",
            // Fictional values: the API validates its JWT settings at start-up even though this test signs nobody in.
            ["Jwt:Issuer"] = "https://apexpost.test/issuer",
            ["Jwt:Audience"] = "apexpost-integration-tests",
            ["Jwt:SigningKey"] = "test-only-signing-key-not-a-secret-0123456789",
            ["Partner:ApiKeys:0"] = "test-partner-key-fictional-0001",
        }));
        builder.ConfigureTestServices(services =>
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { }));
    }

    /// <summary>A client that acts as the given role and courier (no headers = anonymous).</summary>
    public HttpClient ClientAs(string? role, string? courierId = null)
    {
        var client = CreateClient();
        if (role is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, role);
        }

        if (courierId is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.CourierIdHeader, courierId);
        }

        return client;
    }
}

/// <summary>
/// Test-only authentication: builds the caller from two request headers, using the same claim types the JWT handler
/// produces ("role", "courierId", "sub"), so [Authorize(Roles = ...)] and ScanOwnershipFilter behave exactly as in
/// production. A request without the role header is anonymous (NoResult), so the API's own 401 path runs.
/// </summary>
public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string RoleHeader = "X-Test-Role";
    public const string CourierIdHeader = "X-Test-CourierId";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(RoleHeader, out var role) || string.IsNullOrWhiteSpace(role))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var courierId = Request.Headers[CourierIdHeader].ToString();
        List<Claim> claims = [new(ApiRoles.RoleClaim, role.ToString()), new(ApiRoles.SubjectClaim, string.IsNullOrEmpty(courierId) ? "test-user" : courierId)];
        if (!string.IsNullOrEmpty(courierId))
        {
            claims.Add(new Claim(ApiRoles.CourierIdClaim, courierId));
        }

        var identity = new ClaimsIdentity(claims, SchemeName, ApiRoles.SubjectClaim, ApiRoles.RoleClaim);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
