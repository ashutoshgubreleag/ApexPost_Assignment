using ApexPost.Api.Infrastructure;
using ApexPost.Repositories;
using ApexPost.Services;
using ApexPost.Utilities.Middleware;
using System.Text.Json.Serialization;

// Minimal hosting: composition root and pipeline only. No business logic lives here.
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);
builder.Host.UseDefaultServiceProvider(options =>
{
    // Fail fast if a singleton ever captures a scoped service (DbContext, repositories, services).
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});

// Week 3 layers, registered through their existing extension methods.
builder.Services.AddApexPostServices(builder.Configuration);
builder.Services.AddApexPostPersistence(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var environment = sp.GetRequiredService<IHostEnvironment>();
    return new PersistenceOptions
    {
        // User Secrets in Development, environment variable ConnectionStrings__ApexPostDb elsewhere. Never appsettings.json.
        ConnectionString = config.GetConnectionString("ApexPostDb") ?? string.Empty,
        LogSqlCommands = config.GetValue("Database:LogSqlCommands", environment.IsDevelopment()),
        MaxRetryCount = config.GetValue("Database:MaxRetryCount", 3),
    };
});

builder.Services.AddControllers()
    // Enums travel as names ("InTransit"), not numbers; numbers are still accepted on input.
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemDetailsCustomizer.Apply);
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();   // correlation id on every response and log line
app.UseExceptionHandler();                      // unhandled exceptions -> ApiExceptionHandler -> 500 ProblemDetails
app.UseStatusCodePages();                       // bodiless 404/405/415 -> ProblemDetails
app.UseHttpsRedirection();
app.MapControllers();

await app.RunAsync();

/// <summary>Entry point type, public so WebApplicationFactory can host the app in tests.</summary>
public partial class Program;
