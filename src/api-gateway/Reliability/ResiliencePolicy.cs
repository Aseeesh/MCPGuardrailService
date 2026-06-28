namespace GuardrailApi.Reliability;

public static class ResiliencePolicy
{
    public static IHttpClientBuilder AddGuardrailResilience(
        this IHttpClientBuilder builder,
        string serviceName,
        int retryCount = 3,
        int circuitBreakerThreshold = 5,
        int timeoutSeconds = 30)
    {
        builder.ConfigureHttpClient(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        });

        builder.AddStandardResilienceHandler(options =>
        {
            // Retry with exponential backoff
            options.Retry.MaxRetryAttempts = retryCount;
            options.Retry.Delay = TimeSpan.FromMilliseconds(500);
            options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
            options.Retry.UseJitter = true;
            options.Retry.ShouldHandle = static args =>
                ValueTask.FromResult(args.Outcome.Result?.IsSuccessStatusCode != true);

            // Circuit breaker
            options.CircuitBreaker.FailureRatio = 0.5;
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            options.CircuitBreaker.MinimumThroughput = circuitBreakerThreshold;
            options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);

            // Timeout per attempt
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            // Total request timeout
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(timeoutSeconds * (retryCount + 1));
        });

        return builder;
    }
}

public class GracefulShutdownService : IHostedService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<GracefulShutdownService> _logger;

    public GracefulShutdownService(IHostApplicationLifetime lifetime, ILogger<GracefulShutdownService> logger)
    {
        _lifetime = lifetime;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _lifetime.ApplicationStarted.Register(() =>
            _logger.LogInformation("Guardrail API started"));

        _lifetime.ApplicationStopping.Register(() =>
        {
            _logger.LogInformation("Guardrail API shutting down — draining requests");
            Thread.Sleep(5000); // Allow in-flight requests to complete
        });

        _lifetime.ApplicationStopped.Register(() =>
            _logger.LogInformation("Guardrail API stopped"));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

public static class HealthCheckExtensions
{
    public static IServiceCollection AddGuardrailHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHealthChecks()
            .AddNpgSql(
                configuration.GetConnectionString("Postgres")!,
                name: "postgres",
                tags: ["db", "ready"])
            .AddRedis(
                configuration["Redis:ConnectionString"]!,
                name: "redis",
                tags: ["cache", "ready"])
            .AddUrlGroup(
                new Uri($"{configuration["AiService:BaseUrl"]}/health"),
                name: "ai-service",
                tags: ["service", "ready"])
            .AddUrlGroup(
                new Uri("http://localhost:8181/health"),
                name: "opa",
                tags: ["service", "ready"]);

        return services;
    }
}
