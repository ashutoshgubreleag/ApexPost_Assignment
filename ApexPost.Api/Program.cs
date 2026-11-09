using ApexPost.Api.Infrastructure;
using ApexPost.Api.OpenApi;
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
    // (v1 shipments are the exception: their response DTO declares status as an int, see VERSIONING.md.)
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// The OpenAPI generator reads the HTTP JSON options, so give it the same enum rule: schemas show string enums.
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddApexPostVersioning();
builder.Services.AddApexPostOpenApi();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemDetailsCustomizer.Apply);
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();   // correlation id on every response and log line
app.UseExceptionHandler();                      // unhandled exceptions -> ApiExceptionHandler -> 500 ProblemDetails
app.UseStatusCodePages();                       // bodiless 404/405/415 -> ProblemDetails
app.UseHttpsRedirection();

if (app.Environment.IsDevelopment())
{
    // Contract documents and the viewer are Development-only: nothing about the API surface is published by accident.
    app.MapOpenApi();   // /openapi/v1.json and /openapi/v2.json
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "swagger";
        options.DocumentTitle = "ApexPost Logistics API";
        options.SwaggerEndpoint("/openapi/v2.json", "v2");
        options.SwaggerEndpoint("/openapi/v1.json", "v1 (deprecated)");
    });
}

app.MapControllers();

await app.RunAsync();

/// <summary>Entry point type, public so WebApplicationFactory can host the app in tests.</summary>
public partial class Program;
