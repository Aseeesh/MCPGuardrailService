using System.Collections.Concurrent;
using System.Text.Json;
using StackExchange.Redis;

namespace GuardrailApi.Performance.Caching;

public record CacheStats(
    long Hits,
    long Misses,
    double HitRate,
    int LruSize,
    int LruCapacity);

public class PolicyCache
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ConcurrentDictionary<string, LruEntry> _lru = new();
    private readonly ConcurrentQueue<string> _lruOrder = new();
    private readonly int _lruCapacity;
    private readonly TimeSpan _redisTtl;
    private readonly TimeSpan _lruTtl;
    private long _hits;
    private long _misses;

    public PolicyCache(
        IConnectionMultiplexer redis,
        int lruCapacity = 10_000,
        int redisTtlSeconds = 300,
        int lruTtlSeconds = 60)
    {
        _redis = redis;
        _lruCapacity = lruCapacity;
        _redisTtl = TimeSpan.FromSeconds(redisTtlSeconds);
        _lruTtl = TimeSpan.FromSeconds(lruTtlSeconds);
    }

    public async Task<T?> GetOrSetAsync<T>(
        string key,
        Func<Task<T>> factory,
        string? policyVersion = null) where T : class
    {
        var fullKey = policyVersion != null ? $"{key}:v:{policyVersion}" : key;

        // L1: In-memory LRU
        if (_lru.TryGetValue(fullKey, out var lruEntry) && !lruEntry.IsExpired)
        {
            Interlocked.Increment(ref _hits);
            return JsonSerializer.Deserialize<T>(lruEntry.Value);
        }

        // L2: Redis
        var db = _redis.GetDatabase();
        var cached = await db.StringGetAsync($"guardrail:{fullKey}");
        if (cached.HasValue)
        {
            Interlocked.Increment(ref _hits);
            var value = cached.ToString();
            SetLru(fullKey, value);
            return JsonSerializer.Deserialize<T>(value);
        }

        // Cache miss — compute
        Interlocked.Increment(ref _misses);
        var result = await factory();
        if (result == null) return null;

        var serialized = JsonSerializer.Serialize(result);

        // Store in both layers
        SetLru(fullKey, serialized);
        await db.StringSetAsync($"guardrail:{fullKey}", serialized, _redisTtl);

        return result;
    }

    public async Task InvalidateAsync(string keyPattern)
    {
        // Clear LRU entries matching pattern
        foreach (var key in _lru.Keys)
        {
            if (key.Contains(keyPattern))
                _lru.TryRemove(key, out _);
        }

        // Clear Redis entries
        var db = _redis.GetDatabase();
        var server = _redis.GetServers()[0];
        await foreach (var key in server.KeysAsync(pattern: $"guardrail:*{keyPattern}*"))
        {
            await db.KeyDeleteAsync(key);
        }
    }

    public async Task InvalidateByVersionAsync(string policyVersion)
    {
        await InvalidateAsync($":v:{policyVersion}");
    }

    public CacheStats GetStats()
    {
        var total = _hits + _misses;
        return new CacheStats(
            Hits: _hits,
            Misses: _misses,
            HitRate: total > 0 ? Math.Round((double)_hits / total * 100, 2) : 0,
            LruSize: _lru.Count,
            LruCapacity: _lruCapacity);
    }

    public void ResetStats()
    {
        Interlocked.Exchange(ref _hits, 0);
        Interlocked.Exchange(ref _misses, 0);
    }

    private void SetLru(string key, string value)
    {
        _lru[key] = new LruEntry(value, DateTime.UtcNow.Add(_lruTtl));
        _lruOrder.Enqueue(key);

        // Evict if over capacity
        while (_lru.Count > _lruCapacity && _lruOrder.TryDequeue(out var evictKey))
        {
            _lru.TryRemove(evictKey, out _);
        }
    }

    private record LruEntry(string Value, DateTime ExpiresAt)
    {
        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    }
}

public static class PolicyCacheKeys
{
    public static string Evaluation(string contentHash, string frameworks)
        => $"eval:{contentHash}:{frameworks}";

    public static string OpaPolicy(string packageName)
        => $"opa:{packageName}";

    public static string DetectionResult(string contentHash)
        => $"det:{contentHash}";

    public static string ComplianceReport(string framework, string tenantId)
        => $"report:{framework}:{tenantId}";
}
