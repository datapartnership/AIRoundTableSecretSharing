namespace AIRoundTableSecretSharingAPI.Middleware;

/// <summary>
/// Middleware that validates API keys for all /api/ endpoints.
/// Each partner has a unique API key configured in appsettings.json under "ApiKeys".
/// The key must be provided via the X-API-Key header.
/// On successful validation, the authenticated partner ID is stored in HttpContext.Items["AuthenticatedPartnerId"].
/// </summary>
public class ApiKeyAuthMiddleware
{
    private const string ApiKeyHeaderName = "X-API-Key";
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyAuthMiddleware> _logger;

    public ApiKeyAuthMiddleware(RequestDelegate next, ILogger<ApiKeyAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IConfiguration configuration)
    {
        // Only apply to /api/ endpoints
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var apiKeyHeader))
        {
            _logger.LogWarning("API request without API key from {RemoteIp}", context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "API key is required. Provide it via the X-API-Key header." });
            return;
        }

        var apiKey = apiKeyHeader.ToString();
        var apiKeys = configuration.GetSection("ApiKeys").Get<Dictionary<string, string>>();

        if (apiKeys == null || apiKeys.Count == 0)
        {
            _logger.LogError("No API keys configured in appsettings.json");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = "Server misconfiguration: no API keys defined." });
            return;
        }

        var matchingPartner = apiKeys.FirstOrDefault(kvp => kvp.Value == apiKey);

        if (matchingPartner.Key == null)
        {
            _logger.LogWarning("Invalid API key attempt from {RemoteIp}", context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid API key." });
            return;
        }

        // Store the authenticated partner ID for downstream use
        context.Items["AuthenticatedPartnerId"] = matchingPartner.Key;

        _logger.LogDebug("Authenticated request from partner {PartnerId}", matchingPartner.Key);

        await _next(context);
    }
}

/// <summary>
/// Extension method for registering the API key middleware.
/// </summary>
public static class ApiKeyAuthMiddlewareExtensions
{
    public static IApplicationBuilder UseApiKeyAuth(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ApiKeyAuthMiddleware>();
    }
}
