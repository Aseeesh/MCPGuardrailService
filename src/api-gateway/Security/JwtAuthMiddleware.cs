using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace GuardrailApi.Security;

public class JwtAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly JwtSettings _settings;

    public JwtAuthMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _settings = configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Skip auth for health, swagger, metrics
        if (path.StartsWith("/health") || path.StartsWith("/swagger") || path.StartsWith("/api/metrics/prometheus"))
        {
            await _next(context);
            return;
        }

        var token = context.Request.Headers.Authorization
            .FirstOrDefault()?.Replace("Bearer ", "");

        if (string.IsNullOrEmpty(token))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Missing authorization token" });
            return;
        }

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));

            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidIssuer = _settings.Issuer,
                ValidateAudience = true,
                ValidAudience = _settings.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1),
            }, out _);

            context.Items["TenantId"] = principal.FindFirst("tenant_id")?.Value;
            context.Items["UserId"] = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            context.Items["Role"] = principal.FindFirst(ClaimTypes.Role)?.Value;

            await _next(context);
        }
        catch (SecurityTokenException)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid or expired token" });
        }
    }
}

public class JwtSettings
{
    public string Secret { get; set; } = "change-this-in-production-minimum-32-characters";
    public string Issuer { get; set; } = "guardrail-service";
    public string Audience { get; set; } = "guardrail-api";
    public int ExpiryMinutes { get; set; } = 60;
}

public static class JwtTokenGenerator
{
    public static string Generate(JwtSettings settings, string userId, string tenantId, string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim("tenant_id", tenantId),
            new Claim(ClaimTypes.Role, role),
        };

        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(settings.ExpiryMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
