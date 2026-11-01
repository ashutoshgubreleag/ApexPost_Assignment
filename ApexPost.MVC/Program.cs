using ApexPost.MVC.Composition;
using ApexPost.Utilities.Middleware;

// Composition root and pipeline only: no business logic lives here.
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);
builder.Host.UseDefaultServiceProvider(options =>
{
    // Fail fast if a singleton ever captures a scoped service.
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});

builder.Services.AddApexPostWeb(builder.Configuration, builder.Environment);

var app = builder.Build();

// ---- Middleware pipeline (order matters; DESIGN.md explains each position) ----
app.UseForwardedHeaders();                               // 1. real client IP, only from trusted proxies
app.UseMiddleware<CorrelationIdMiddleware>();            // 2. correlation ID + log scope for everything below
app.UseMiddleware<RequestLoggingMiddleware>();           // 3. timing/logging; outside the handler so it sees the final status
app.UseExceptionHandler("/Home/Error");                  // 4. Problem Details / error page, no stack traces
app.UseStatusCodePagesWithReExecute("/Home/StatusCode/{0}");
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();                                       // 5. HSTS outside Development
}

app.UseHttpsRedirection();                               // 6. HTTPS only
app.UseMiddleware<SecurityHeadersMiddleware>();          // 7. CSP, nosniff, frame and referrer policy on every response
app.UseMiddleware<MaintenanceModeMiddleware>();          // 8. 503 + Retry-After (keeps /health)
app.UseRouting();                                        // 9. endpoint selected; metadata now visible
app.UseCors();                                           // 10. partner widget origin only; answers preflights
app.UseMiddleware<IpRestrictionMiddleware>();            // 11. admin/integration allowlist, 403
app.UseMiddleware<ApiKeyValidationMiddleware>();         // 12. X-Api-Key on [ApiKeyRequired] endpoints, 401/500
app.UseMiddleware<DeviceAuthenticationMiddleware>();     // 12b. X-Device-Id/X-Device-Key on [DeviceAuthenticated] (/api/sync), 401
app.UseRateLimiter();                                    // 13. login brute force + per-partner budget, 429
app.UseAuthentication();                                 // 14. cookie → ClaimsPrincipal
app.UseAuthorization();                                  // 15. policies (fallback: authenticated)

app.MapHealthChecks("/health").AllowAnonymous();
app.MapStaticAssets().AllowAnonymous();
app.MapControllerRoute(
    name: "integration",
    pattern: "integration/parcel-status/{trackingNumber}",
    defaults: new { controller = "Integration", action = "ParcelStatus" });
app.MapControllerRoute(
    name: "branch",
    pattern: "Branch/{action}/{branchCode:regex(^BR-[0-9]{{3}}$)}",
    defaults: new { controller = "Branch" });
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

await app.RunAsync();

/// <summary>Entry point type, public so WebApplicationFactory can host the app in tests.</summary>
public partial class Program;
