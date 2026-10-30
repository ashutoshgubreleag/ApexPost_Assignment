namespace ApexPost.Utilities;

/// <summary>
/// Endpoint metadata: the endpoint is called by courier partners and needs a valid
/// <c>X-Api-Key</c>. <see cref="Middleware.ApiKeyValidationMiddleware"/> reads it after routing
/// via <c>HttpContext.GetEndpoint()</c>, so protection is declared next to the action rather than in
/// a list of paths that can drift.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ApiKeyRequiredAttribute : Attribute
{
}

/// <summary>Endpoint metadata: only callers on the configured IP allowlist may reach this endpoint.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class IpRestrictedAttribute : Attribute
{
}

/// <summary>
/// Marks a conventionally routed controller as a JSON API, so filters and the exception handler
/// answer with Problem Details instead of redirects or HTML error pages.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class JsonApiAttribute : Attribute
{
}

/// <summary>
/// Marks counter-terminal endpoints (/api/sync). <see cref="Middleware.DeviceAuthenticationMiddleware"/>
/// requires the X-Device-Id and X-Device-Key headers and binds the request to the device's branch.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class DeviceAuthenticatedAttribute : Attribute
{
}
