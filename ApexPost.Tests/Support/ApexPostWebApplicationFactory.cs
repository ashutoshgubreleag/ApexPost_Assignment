using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ApexPost.Services.Abstractions;
using ApexPost.Tests.Support.Fakes;

namespace ApexPost.Tests.Support;

/// <summary>
/// Hosts the real app in memory with a private data folder and test configuration.
/// TestServer has no real socket, so <c>RemoteIpAddress</c> is null; a test-only startup filter
/// sets it from <c>X-Test-Client-IP</c> (default 127.0.0.1) so IP restriction can be exercised.
/// This header means nothing to the production app, which never registers the filter.
/// </summary>
public sealed partial class ApexPostWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestApiKey = "test-partner-key-0123456789";
    public const string TestClientIpHeader = "X-Test-Client-IP";
    public const string TestDeviceKey = "test-device-key-0123456789";

    private readonly Dictionary<string, string?> _overrides;

    /// <summary>Default test host (used as an xUnit class fixture, which needs one public constructor).</summary>
    public ApexPostWebApplicationFactory() : this(null, "Testing") { }

    private ApexPostWebApplicationFactory(Dictionary<string, string?>? overrides, string environment)
    {
        _overrides = overrides ?? [];
        Environment = environment;
    }

    /// <summary>A test host with configuration overrides.</summary>
    public static ApexPostWebApplicationFactory With(Dictionary<string, string?> overrides, string environment = "Testing") => new(overrides, environment);

    public string Environment { get; }

    /// <summary>The in-memory "database" behind this test server (the pipeline tests need no SQL Server).</summary>
    public InMemoryStore Store { get; } = new();

    /// <summary>Audit entries written by the audit filter during the test.</summary>
    public IReadOnlyList<AuditEntry> AuditEntries => Services.GetRequiredService<InMemoryAuditStore>().Entries;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment(Environment);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:ApexPostDb"] = "Server=unused-in-pipeline-tests",
                ["Sync:Devices:TERM-TEST-01:Key"] = TestDeviceKey,
                ["Sync:Devices:TERM-TEST-01:BranchCode"] = "BR-001",
                ["Sync:Devices:TERM-TEST-02:Key"] = TestDeviceKey + "-2",
                ["Sync:Devices:TERM-TEST-02:BranchCode"] = "BR-002",
                ["Integration:Partners:swiftcourier"] = TestApiKey,
                ["IpRestriction:Enabled"] = "true",
                ["IpRestriction:AllowList:0"] = "127.0.0.1",
                ["IpRestriction:AllowList:1"] = "::1",
                ["RateLimiting:LoginPermitLimit"] = "100",
                ["Courier:RateLookupLatencyMs"] = "1",
                ["Courier:TrackingLatencyMs"] = "1",
                ["Courier:TrackingTimeoutMs"] = "250",
                ["Logging:LogLevel:Default"] = "Warning",
            };
            foreach (var (key, value) in _overrides)
            {
                settings[key] = value;
            }

            config.AddInMemoryCollection(settings);
        });
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IStartupFilter, TestClientIpStartupFilter>();

            // Replace the EF Core persistence layer with the in-memory fakes (last registration wins).
            services.AddInMemoryPersistence(Store);
            services.Configure<HealthCheckServiceOptions>(options =>
            {
                options.Registrations.Clear();
                options.Registrations.Add(new HealthCheckRegistration("in-memory", _ => new AlwaysHealthy(), null, null));
            });
            services.AddControllersWithViews().AddApplicationPart(typeof(ThrowController).Assembly);
        });
    }

    /// <summary>HTTPS client that keeps cookies and does not follow redirects.</summary>
    public HttpClient CreateBrowser(string? clientIp = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });
        if (clientIp is not null)
        {
            client.DefaultRequestHeaders.Add(TestClientIpHeader, clientIp);
        }

        client.DefaultRequestHeaders.Accept.ParseAdd("text/html");
        return client;
    }

    public static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string path)
    {
        ArgumentNullException.ThrowIfNull(client);
        var html = await client.GetStringAsync(new Uri(path, UriKind.Relative));
        var match = TokenPattern().Match(html);
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : throw new InvalidOperationException($"No antiforgery token on {path}.");
    }

    public static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string formPage, string action, Dictionary<string, string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        fields["__RequestVerificationToken"] = await GetAntiforgeryTokenAsync(client, formPage);
        using var content = new FormUrlEncodedContent(fields);
        return await client.PostAsync(new Uri(action, UriKind.Relative), content);
    }

    public static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password = "ApexPost123!") =>
        await PostFormAsync(client, "/Account/Login", "/Account/Login", new Dictionary<string, string> { ["Email"] = email, ["Password"] = password });


    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    private sealed class TestClientIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                var header = context.Request.Headers[TestClientIpHeader].ToString();
                context.Connection.RemoteIpAddress = IPAddress.TryParse(header, out var ip) ? ip : IPAddress.Loopback;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}

/// <summary>Test-only controller that throws, to prove the exception handler hides details.</summary>
[Microsoft.AspNetCore.Authorization.AllowAnonymous]
public sealed class ThrowController : Microsoft.AspNetCore.Mvc.Controller
{
    public Microsoft.AspNetCore.Mvc.IActionResult Now() => throw new InvalidOperationException("Sensitive internal detail: connection string=secret");
}

internal sealed class AlwaysHealthy : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(HealthCheckResult.Healthy("In-memory store."));
}
