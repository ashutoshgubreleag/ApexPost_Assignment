using Asp.Versioning;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ApexPost.Api.OpenApi;

/// <summary>Registration of API versioning and the version-specific OpenAPI documents.</summary>
internal static class OpenApiServiceCollectionExtensions
{
    /// <summary>
    /// URL-segment versioning (<c>/api/v1/...</c>, <c>/api/v2/...</c>) with version headers on every response
    /// (<c>api-supported-versions</c>, <c>api-deprecated-versions</c>).
    /// </summary>
    public static IServiceCollection AddApexPostVersioning(this IServiceCollection services)
    {
        services
            .AddApiVersioning(options =>
            {
                options.DefaultApiVersion = new ApiVersion(2, 0);
                options.AssumeDefaultVersionWhenUnspecified = false; // the version is part of the URL, never guessed
                options.ReportApiVersions = true;
                options.ApiVersionReader = new UrlSegmentApiVersionReader();
            })
            .AddMvc()
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'V";       // group names "v1" and "v2" = OpenAPI document names
                options.SubstituteApiVersionInUrl = true; // paths in the documents read /api/v1/..., not /api/v{version}/...
            });
        return services;
    }

    /// <summary>One OpenAPI document per API version, built in by ASP.NET Core (no Swashbuckle generator).</summary>
    public static IServiceCollection AddApexPostOpenApi(this IServiceCollection services)
    {
        foreach (var documentName in new[] { ApexPostOpenApiTransformer.V1, ApexPostOpenApiTransformer.V2 })
        {
            services.AddOpenApi(documentName, options =>
            {
                // OpenAPI 3.0 keeps the document readable by every generator (NSwag included).
                options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
                options.AddDocumentTransformer<ApexPostOpenApiTransformer>();
                options.AddOperationTransformer<ApexPostOpenApiTransformer>();
            });
        }

        return services;
    }
}
