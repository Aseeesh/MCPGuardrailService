using System.Collections.Concurrent;
using System.Diagnostics;

namespace GuardrailApi.Performance.Monitoring;

public record LatencyBucket(
    double P50,
    double P95,
    double P99,
    double Avg,
    double Min,
    double Max,
    int SampleCount);

public record PipelineMetrics(
    LatencyBucket Deterministic,
    LatencyBucket OpaCheck,
    LatencyBucket LlmJudge,
    LatencyBucket TotalPipeline,
    long TotalRequests,
    long Errors,
    double ErrorRate,
    Dictionary<string, int> DecisionCounts,
    CacheMetrics Cache,
    QueueMetrics Queues);

public record CacheMetrics(
    long Hits,
    long Misses,
    double HitRate);

public record QueueMetrics(
    int LlmPending,
    int AuditPending,
    int RemediationPending);

public class PerformanceTracker
{
    private readonly ConcurrentDictionary<string, LatencyTracker> _trackers = new();
    private readonly ConcurrentDictionary<string, int> _decisions = new();
    private long _totalRequests;
    private long _errors;

    public IDisposable Track(string stage)
    {
        var tracker = _trackers.GetOrAdd(stage, _ => new LatencyTracker());
        return new TimerScope(tracker);
    }

    public void RecordRequest(string decision, bool isError = false)
    {
        Interlocked.Increment(ref _totalRequests);
        if (isError) Interlocked.Increment(ref _errors);
        _decisions.AddOrUpdate(decision, 1, (_, v) => v + 1);
    }

    public LatencyBucket GetLatency(string stage)
    {
        if (!_trackers.TryGetValue(stage, out var tracker))
            return new LatencyBucket(0, 0, 0, 0, 0, 0, 0);
        return tracker.GetBucket();
    }

    public PipelineMetrics GetMetrics(Caching.CacheStats? cacheStats = null)
    {
        var total = _totalRequests;
        return new PipelineMetrics(
            Deterministic: GetLatency("deterministic"),
            OpaCheck: GetLatency("opa"),
            LlmJudge: GetLatency("llm_judge"),
            TotalPipeline: GetLatency("total"),
            TotalRequests: total,
            Errors: _errors,
            ErrorRate: total > 0 ? Math.Round((double)_errors / total * 100, 3) : 0,
            DecisionCounts: new Dictionary<string, int>(_decisions),
            Cache: new CacheMetrics(
                cacheStats?.Hits ?? 0,
                cacheStats?.Misses ?? 0,
                cacheStats?.HitRate ?? 0),
            Queues: new QueueMetrics(0, 0, 0));
    }

    public string GetPrometheusMetrics()
    {
        var sb = new System.Text.StringBuilder();

        foreach (var (stage, tracker) in _trackers)
        {
            var bucket = tracker.GetBucket();
            sb.AppendLine($"# HELP guardrail_{stage}_latency_ms Latency in milliseconds");
            sb.AppendLine($"# TYPE guardrail_{stage}_latency_ms summary");
            sb.AppendLine($"guardrail_{stage}_latency_ms{{quantile=\"0.5\"}} {bucket.P50}");
            sb.AppendLine($"guardrail_{stage}_latency_ms{{quantile=\"0.95\"}} {bucket.P95}");
            sb.AppendLine($"guardrail_{stage}_latency_ms{{quantile=\"0.99\"}} {bucket.P99}");
            sb.AppendLine($"guardrail_{stage}_latency_ms_count {bucket.SampleCount}");
        }

        sb.AppendLine($"# HELP guardrail_requests_total Total requests");
        sb.AppendLine($"# TYPE guardrail_requests_total counter");
        sb.AppendLine($"guardrail_requests_total {_totalRequests}");

        sb.AppendLine($"# HELP guardrail_errors_total Total errors");
        sb.AppendLine($"# TYPE guardrail_errors_total counter");
        sb.AppendLine($"guardrail_errors_total {_errors}");

        foreach (var (decision, count) in _decisions)
        {
            sb.AppendLine($"guardrail_decisions_total{{decision=\"{decision}\"}} {count}");
        }

        return sb.ToString();
    }

    public void Reset()
    {
        _trackers.Clear();
        _decisions.Clear();
        Interlocked.Exchange(ref _totalRequests, 0);
        Interlocked.Exchange(ref _errors, 0);
    }
}

public class LatencyTracker
{
    private readonly ConcurrentQueue<double> _samples = new();
    private const int MaxSamples = 10_000;

    public void Record(double ms)
    {
        _samples.Enqueue(ms);
        while (_samples.Count > MaxSamples)
            _samples.TryDequeue(out _);
    }

    public LatencyBucket GetBucket()
    {
        var samples = _samples.ToArray();
        if (samples.Length == 0)
            return new LatencyBucket(0, 0, 0, 0, 0, 0, 0);

        Array.Sort(samples);
        return new LatencyBucket(
            P50: Math.Round(Percentile(samples, 0.50), 2),
            P95: Math.Round(Percentile(samples, 0.95), 2),
            P99: Math.Round(Percentile(samples, 0.99), 2),
            Avg: Math.Round(samples.Average(), 2),
            Min: Math.Round(samples.Min(), 2),
            Max: Math.Round(samples.Max(), 2),
            SampleCount: samples.Length);
    }

    private static double Percentile(double[] sorted, double p)
    {
        var idx = (int)Math.Ceiling(p * sorted.Length) - 1;
        return sorted[Math.Max(0, Math.Min(idx, sorted.Length - 1))];
    }
}

internal class TimerScope : IDisposable
{
    private readonly LatencyTracker _tracker;
    private readonly Stopwatch _sw;

    public TimerScope(LatencyTracker tracker)
    {
        _tracker = tracker;
        _sw = Stopwatch.StartNew();
    }

    public void Dispose()
    {
        _sw.Stop();
        _tracker.Record(_sw.Elapsed.TotalMilliseconds);
    }
}
