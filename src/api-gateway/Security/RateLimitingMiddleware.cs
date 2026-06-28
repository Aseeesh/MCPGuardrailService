using System.Collections.Concurrent;
using StackExchange.Redis;

namespace GuardrailApi.Security;

public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RateLimitSettings _settings;
    private readonly IConnectionMultiplexer? _redis;

    // In-memory fallback when Redis unavailable
    private static readonly ConcurrentDictionary<string, SlidingWindow> _localWindows = new();

    public RateLimitingMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        IConnectionMultiplexer? redis = null)
    {
        _next = next;
        _settings = configuration.GetSection("RateLimit").Get<RateLimitSettings>() ?? new();
        _redis = redis;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        if (path.StartsWith("/health") || path.StartsWith("/swagger"))
        {
            await _next(context);
            return;
        }

        var clientKey = GetClientKey(context);
        var limit = GetLimit(path);

        bool allowed;
        int remaining;
        int resetSeconds;

        if (_redis != null)
        {
            (allowed, remaining, resetSeconds) = await CheckRedisRateLimit(clientKey, limit);
        }
        else
        {
            (allowed, remaining, resetSeconds) = CheckLocalRateLimit(clientKey, limit);
        }

        context.Response.Headers["X-RateLimit-Limit"] = limit.MaxRequests.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = remaining.ToString();
        context.Response.Headers["X-RateLimit-Reset"] = resetSeconds.ToString();

        if (!allowed)
        {
            context.Response.StatusCode = 429;
            context.Response.Headers["Retry-After"] = resetSeconds.ToString();
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Rate limit exceeded",
                retry_after = resetSeconds,
                limit = limit.MaxRequests,
                window_seconds = limit.WindowSeconds,
            });
            return;
        }

        await _next(context);
    }

    private async Task<(bool Allowed, int Remaining, int ResetSeconds)> CheckRedisRateLimit(
        string key, RateLimit limit)
    {
        var db = _redis!.GetDatabase();
        var redisKey = $"ratelimit:{key}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var windowStart = now - limit.WindowSeconds;

        // Sliding window using sorted set
        var pipe = db.CreateBatch();
        pipe.SortedSetRemoveRangeByScoreAsync(redisKey, 0, windowStart);
        pipe.SortedSetAddAsync(redisKey, now.ToString(), now);
        pipe.SortedSetLengthAsync(redisKey);
        pipe.KeyExpireAsync(redisKey, TimeSpan.FromSeconds(limit.WindowSeconds + 1));
        pipe.Execute();

        var count = await db.SortedSetLengthAsync(redisKey);
        var remaining = Math.Max(0, limit.MaxRequests - (int)count);
        var allowed = count <= limit.MaxRequests;

        return (allowed, remaining, limit.WindowSeconds);
    }

    private static (bool Allowed, int Remaining, int ResetSeconds) CheckLocalRateLimit(
        string key, RateLimit limit)
    {
        var window = _localWindows.GetOrAdd(key, _ => new SlidingWindow(limit.WindowSeconds));
        window.Slide();

        var allowed = window.Count < limit.MaxRequests;
        if (allowed) window.Increment();

        return (allowed, Math.Max(0, limit.MaxRequests - window.Count), limit.WindowSeconds);
    }

    private static string GetClientKey(HttpContext context)
    {
        var tenantId = context.Items["TenantId"]?.ToString();
        if (!string.IsNullOrEmpty(tenantId)) return $"tenant:{tenantId}";

        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return $"ip:{ip}";
    }

    private RateLimit GetLimit(string path)
    {
        if (path.Contains("/pipeline/detect") || path.Contains("/remediation/process"))
            return new RateLimit(_settings.PipelineMaxPerMinute, 60);
        if (path.Contains("/workflow/run"))
            return new RateLimit(_settings.WorkflowMaxPerMinute, 60);
        if (path.Contains("/llm/judge"))
            return new RateLimit(_settings.LlmMaxPerMinute, 60);
        return new RateLimit(_settings.DefaultMaxPerMinute, 60);
    }
}

public class RateLimitSettings
{
    public int DefaultMaxPerMinute { get; set; } = 120;
    public int PipelineMaxPerMinute { get; set; } = 300;
    public int WorkflowMaxPerMinute { get; set; } = 30;
    public int LlmMaxPerMinute { get; set; } = 20;
}

record RateLimit(int MaxRequests, int WindowSeconds);

class SlidingWindow
{
    private int _count;
    private DateTime _windowStart;
    private readonly int _windowSeconds;

    public SlidingWindow(int windowSeconds)
    {
        _windowSeconds = windowSeconds;
        _windowStart = DateTime.UtcNow;
    }

    public int Count => _count;

    public void Slide()
    {
        if ((DateTime.UtcNow - _windowStart).TotalSeconds >= _windowSeconds)
        {
            _count = 0;
            _windowStart = DateTime.UtcNow;
        }
    }

    public void Increment() => Interlocked.Increment(ref _count);
}
