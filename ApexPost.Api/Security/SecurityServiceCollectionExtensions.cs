using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ApexPost.Api.Security;

/// <summary>Registration of JWT bearer authentication, authorization, the partner API key filter and the dev token service.</summary>
internal static class SecurityServiceCollectionExtensions
{
    /// <summary>
    /// Adds everything Part D needs on the server side. Settings are read lazily through the options system (not at
    /// registration time), so configuration supplied by a test host or by user-secrets is always honoured.
    /// </summary>
    public static IServiceCollection AddApexPostSecurity(this IServiceCollection services)
    {
        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer) && !string.IsNullOrWhiteSpace(o.Audience),
                "Jwt:Issuer and Jwt:Audience must be configured (see appsettings.json).")
            .Validate(o => o.SigningKey is { Length: >= JwtOptions.MinimumSigningKeyLength },
                $"Jwt:SigningKey is missing or shorter than {JwtOptions.MinimumSigningKeyLength} characters. It is a secret and is never committed: "
                + "set it with 'dotnet user-secrets set \"Jwt:SigningKey\" \"<at least 32 random characters>\" --project .\\ApexPost.Api' "
                + "or the environment variable Jwt__SigningKey. See SECURITY.md.")
            .ValidateOnStart();

        services.TryAddTimeProviderSingleton();
        services.AddSingleton<ITokenService, JwtTokenService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                bearer.MapInboundClaims = false; // keep "sub", "role" and "courierId" exactly as issued
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(jwt.ClockSkewSeconds),
                    RoleClaimType = ApiRoles.RoleClaim,
                    NameClaimType = ApiRoles.SubjectClaim,
                };
                bearer.Events = new JwtBearerEvents
                {
                    OnChallenge = ProblemDetailsEvents.WriteUnauthorizedAsync,
                    OnForbidden = ProblemDetailsEvents.WriteForbiddenAsync,
                };
            });

        services.AddAuthorization(options =>
        {
            // Every endpoint needs a signed-in caller unless it says [AllowAnonymous] (dev token, OpenAPI, partner route).
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        });

        services.AddScoped<ScanOwnershipFilter>();
        services.AddScoped<PartnerApiKeyFilter>();
        return services;
    }

    private static void TryAddTimeProviderSingleton(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}

/// <summary>401 and 403 written as ProblemDetails through <see cref="IProblemDetailsService"/>, so they carry the same traceId/correlationId as every other error.</summary>
internal static class ProblemDetailsEvents
{
    public static async Task WriteUnauthorizedAsync(JwtBearerChallengeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.HandleResponse(); // we write the body, not the default empty 401

        var http = context.HttpContext;
        http.Response.StatusCode = StatusCodes.Status401Unauthorized;
        http.Response.Headers.WWWAuthenticate = context.AuthenticateFailure is null
            ? "Bearer"
            : context.AuthenticateFailure is SecurityTokenExpiredException
                ? "Bearer error=\"invalid_token\", error_description=\"The token has expired\""
                : "Bearer error=\"invalid_token\"";

        await http.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Detail = context.AuthenticateFailure is SecurityTokenExpiredException
                    ? "The access token has expired. Request a new one."
                    : "A valid bearer access token is required.",
            },
        }).ConfigureAwait(false);
    }

    public static async Task WriteForbiddenAsync(ForbiddenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var http = context.HttpContext;
        http.Response.StatusCode = StatusCodes.Status403Forbidden;
        await http.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Forbidden",
                Detail = "Your role is not allowed to perform this action.",
            },
        }).ConfigureAwait(false);
    }
}
