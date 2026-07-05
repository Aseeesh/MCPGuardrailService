using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace GuardrailApi.Observability;

public static class ObservabilitySetup
{
    public static readonly ActivitySource ActivitySource = new("GuardrailApi", "2.0.0");

    public static IServiceCollection AddGuardrailObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Structured logging with Serilog-style output
        services.AddLogging(builder =>
        {
            builder.AddJsonConsole(options =>
            {
                options.IncludeScopes = true;
                options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
                options.UseUtcTimestamp = true;
                options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions
                {
                    Indented = false,
                };
            });
        });

        // OpenTelemetry
        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource("GuardrailApi")
                    .AddAspNetCoreInstrumentation(o =>
                    {
                        o.RecordException = true;
                        o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health");
                    })
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(o =>
                    {
                        o.Endpoint = new Uri(configuration["Otlp:Endpoint"] ?? "http://localhost:4317");
                    });
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("GuardrailApi")
                    .AddOtlpExporter(o =>
                    {
                        o.Endpoint = new Uri(configuration["Otlp:Endpoint"] ?? "http://localhost:4317");
                    });
            });

        return services;
    }
}

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var requestId = context.TraceIdentifier;
        var tenantId = context.Items["TenantId"]?.ToString() ?? "anonymous";

        using var activity = ObservabilitySetup.ActivitySource.StartActivity("http.request");
        activity?.SetTag("tenant.id", tenantId);
        activity?.SetTag("http.method", context.Request.Method);
        activity?.SetTag("http.path", context.Request.Path.Value);

        try
        {
            await _next(context);
            sw.Stop();

            _logger.LogInformation(
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms [tenant={TenantId} request={RequestId}]",
                context.Request.Method,
                context.Request.Path.Value,
                context.Response.StatusCode,
                sw.ElapsedMilliseconds,
                tenantId,
                requestId);

            activity?.SetTag("http.status_code", context.Response.StatusCode);
            activity?.SetTag("http.duration_ms", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "HTTP {Method} {Path} failed after {ElapsedMs}ms [tenant={TenantId} request={RequestId}]",
                context.Request.Method,
                context.Request.Path.Value,
                sw.ElapsedMilliseconds,
                tenantId,
                requestId);

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
