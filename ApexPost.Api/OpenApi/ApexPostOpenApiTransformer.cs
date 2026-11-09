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

        return Task.CompletedTask;
    }
}
