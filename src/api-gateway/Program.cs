using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using StackExchange.Redis;
using GuardrailApi.Pipeline;
using GuardrailApi.Remediation;
using GuardrailApi.Audit;
using GuardrailApi.Security;
using GuardrailApi.Reliability;
using GuardrailApi.Observability;
using GuardrailApi.Performance.Caching;
using GuardrailApi.Performance.Monitoring;

var builder = WebApplication.CreateBuilder(args);

// --- Core Services ---
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddResponseCompression();

// --- Database ---
builder.Services.AddDbContext<GuardrailDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddDbContext<AuditDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// --- Redis ---
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(builder.Configuration["Redis:ConnectionString"]!));

// --- HTTP Clients with Resilience ---
builder.Services.AddHttpClient("AiService", client =>
    client.BaseAddress = new Uri(builder.Configuration["AiService:BaseUrl"]!))
    .AddGuardrailResilience("AiService", retryCount: 3, timeoutSeconds: 30);

builder.Services.AddHttpClient("AzureStorage")
    .AddGuardrailResilience("AzureStorage", retryCount: 2, timeoutSeconds: 10);

// --- Application Services ---
builder.Services.AddSingleton<DetectionPipeline>(sp =>
    new DetectionPipeline(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<IConnectionMultiplexer>()));

builder.Services.AddSingleton<RemediationService>(sp =>
    new RemediationService(sp.GetRequiredService<IHttpClientFactory>()));

builder.Services.AddSingleton<AuditService>(sp =>
    new AuditService(sp.GetRequiredService<IHttpClientFactory>()));

builder.Services.AddSingleton<PolicyCache>(sp =>
    new PolicyCache(sp.GetRequiredService<IConnectionMultiplexer>()));

builder.Services.AddSingleton<PerformanceTracker>();

// --- Reliability ---
builder.Services.AddHostedService<GracefulShutdownService>();
builder.Services.AddGuardrailHealthChecks(builder.Configuration);

// --- Observability ---
builder.Services.AddGuardrailObservability(builder.Configuration);

// --- CORS ---
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
            ?? ["http://localhost:5173", "http://localhost:5174"];
        policy.WithOrigins(origins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });

    options.AddPolicy("Public", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

// --- Middleware Pipeline (order matters) ---
app.UseResponseCompression();

// Security headers first
app.UseMiddleware<SecurityHeadersMiddleware>();

// Request logging
app.UseMiddleware<RequestLoggingMiddleware>();

// Rate limiting before auth
app.UseMiddleware<RateLimitingMiddleware>();

// Authentication (skip in development if configured)
if (!app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Jwt:EnableInDev"))
{
    app.UseMiddleware<JwtAuthMiddleware>();
    app.UseMiddleware<RbacMiddleware>();
}

app.UseCors();

// Swagger (dev/staging only)
if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

// Health checks
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration.TotalMilliseconds,
            }),
            totalDuration = report.TotalDuration.TotalMilliseconds,
        });
    },
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false, // No checks — just confirms process is running
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

// Database migration
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GuardrailDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();

// --- Entities ---

public class GuardrailDbContext : DbContext
{
    public GuardrailDbContext(DbContextOptions<GuardrailDbContext> options) : base(options) { }

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<ComplianceResult> ComplianceResults => Set<ComplianceResult>();
}

public class AuditEntry
{
    public Guid Id { get; set; }
    public required string Action { get; set; }
    public required string Resource { get; set; }
    public required string Result { get; set; }
    public string? Details { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class ComplianceResult
{
    public Guid Id { get; set; }
    public required string Framework { get; set; }
    public required string ControlId { get; set; }
    public required string Status { get; set; }
    public string? Finding { get; set; }
    public string? Remediation { get; set; }
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}
