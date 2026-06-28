namespace GuardrailApi.Security;

public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["X-XSS-Protection"] = "0";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline'; " +
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data:; " +
            "font-src 'self'; " +
            "connect-src 'self'; " +
            "frame-ancestors 'none'";

        if (context.Request.IsHttps)
        {
            headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";
        }

        headers.Remove("Server");
        headers.Remove("X-Powered-By");

        await _next(context);
    }
}

public class RbacMiddleware
{
    private readonly RequestDelegate _next;

    private static readonly Dictionary<string, string[]> RouteRoles = new()
    {
        ["/api/pipeline"] = ["admin", "operator", "analyst"],
        ["/api/remediation"] = ["admin", "operator"],
        ["/api/compliance"] = ["admin", "compliance_officer", "analyst"],
        ["/api/audit"] = ["admin", "compliance_officer", "auditor"],
        ["/api/review"] = ["admin", "reviewer"],
        ["/api/policies"] = ["admin", "policy_manager"],
        ["/api/metrics"] = ["admin", "operator"],
    };

    public RbacMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Skip RBAC for non-API routes
        if (!path.StartsWith("/api/") || path.StartsWith("/api/metrics/prometheus"))
        {
            await _next(context);
            return;
        }

        var role = context.Items["Role"]?.ToString() ?? "";

        // Admin bypasses all
        if (role == "admin")
        {
            await _next(context);
            return;
        }

        foreach (var (route, allowed) in RouteRoles)
        {
            if (path.StartsWith(route) && !allowed.Contains(role))
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Insufficient permissions",
                    required_roles = allowed,
                    your_role = string.IsNullOrEmpty(role) ? "none" : role,
                });
                return;
            }
        }

        await _next(context);
    }
}
