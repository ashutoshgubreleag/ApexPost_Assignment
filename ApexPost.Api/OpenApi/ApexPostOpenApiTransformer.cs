using ApexPost.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ApexPost.Api.OpenApi;

/// <summary>
/// Document and operation transformer shared by the <c>v1</c> and <c>v2</c> OpenAPI documents.
/// It fills in the API title, version, description and a fictional contact, and flags every
/// operation of a deprecated version as <c>deprecated</c> so Swagger UI strikes it through
/// and generated SDKs mark the methods obsolete.
/// </summary>
internal sealed class ApexPostOpenApiTransformer : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
{
    /// <summary>Document names (they match the API explorer group names <c>v1</c> and <c>v2</c>).</summary>
    public const string V1 = "v1";

    /// <inheritdoc cref="V1"/>
    public const string V2 = "v2";

    /// <summary>Name of the bearer (JWT) security scheme.</summary>
    public const string BearerScheme = "Bearer";

    /// <summary>Name of the partner API-key security scheme.</summary>
    public const string ApiKeyScheme = "PartnerApiKey";

    private static bool IsDeprecated(string? documentName) => string.Equals(documentName, V1, StringComparison.Ordinal);

    /// <inheritdoc />
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);

        var deprecated = IsDeprecated(context.DocumentName);
        document.Info = new OpenApiInfo
        {
            Title = "ApexPost Logistics API",
            Version = deprecated ? "1.0" : "2.0",
            Description = deprecated
                ? "**Version 1 is deprecated.** It still works but receives no new features. Use version 2 (see VERSIONING.md for the migration guide). Courier shipment tracking: shipments, status changes and idempotent offline scan upload."
                : "Courier shipment tracking: shipments, status changes and idempotent offline scan upload. Version 2 nests the recipient, returns the latest scan and writes status as a string.",
            Contact = new OpenApiContact
            {
                Name = "ApexPost API team",
                Email = "api-team@apexpost.example",
            },
        };

        // Security schemes (Microsoft.OpenApi 2.x object model). Swagger UI turns these into the Authorize button.
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "JWT access token. In Development get one from POST /dev/token, then paste only the token (Swagger UI adds the Bearer prefix).",
        };
        if (!deprecated)
        {
            document.Components.SecuritySchemes[ApiKeyScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                Name = PartnerApiKeyFilter.HeaderName,
                In = ParameterLocation.Header,
                Description = "Partner API key, for the read-only partner route only. Server-to-server; never put it in a mobile app or a browser.",
            };
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        if (IsDeprecated(context.DocumentName))
        {
            operation.Deprecated = true;
        }

        IList<object> metadata = context.Description.ActionDescriptor.EndpointMetadata ?? [];
        if (metadata.OfType<PartnerApiKeyAttribute>().Any())
        {
            AddRequirement(operation, ApiKeyScheme);
            AddResponse(operation, "401", "Missing or invalid X-Api-Key.");
        }
        else if (metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any())
        {
            AddRequirement(operation, BearerScheme);
            AddResponse(operation, "401", "Missing, invalid or expired bearer token.");
            if (metadata.OfType<IAuthorizeData>().Any(a => !string.IsNullOrEmpty(a.Roles)))
            {
                AddResponse(operation, "403", "The caller's role is not allowed (or, for scan upload, the courierId does not match the token).");
            }
        }

        return Task.CompletedTask;
    }

    private static void AddRequirement(OpenApiOperation operation, string schemeName)
    {
        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(schemeName)] = [] });
    }

    private static void AddResponse(OpenApiOperation operation, string code, string description)
    {
        operation.Responses ??= new OpenApiResponses();
        operation.Responses.TryAdd(code, new OpenApiResponse { Description = description });
    }
}
