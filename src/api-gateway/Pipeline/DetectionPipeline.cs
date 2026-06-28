using System.Diagnostics;
using StackExchange.Redis;

namespace GuardrailApi.Pipeline;

public record DetectionRequest(
    string Content,
    string ResourceType,
    string Source,
    string[] Frameworks,
    Dictionary<string, object>? Context = null);

public record DetectionResult(
    string Decision,
    double Confidence,
    List<Violation> Violations,
    StageTimings Timings,
    string AuditId);

public record Violation(
    string Rule,
    string Type,
    string Severity,
    string Message,
    string Remediation,
    string Stage,
    double Confidence);

public record StageTimings(
    long DeterministicMs,
    long EnsembleMs,
    long LlmJudgeMs,
    long TotalMs);

public class DetectionPipeline
{
    private readonly DeterministicStage _deterministic;
    private readonly EnsembleDecisionEngine _ensemble;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConnectionMultiplexer _redis;
    private readonly CircuitBreaker.CircuitBreaker _llmCircuitBreaker;

    public DetectionPipeline(
        IHttpClientFactory httpClientFactory,
        IConnectionMultiplexer redis)
    {
        _deterministic = new DeterministicStage();
        _ensemble = new EnsembleDecisionEngine();
        _httpClientFactory = httpClientFactory;
        _redis = redis;
        _llmCircuitBreaker = new CircuitBreaker.CircuitBreaker(
            failureThreshold: 5,
            recoveryTimeSeconds: 30);
    }

    public async Task<DetectionResult> ExecuteAsync(DetectionRequest request)
    {
        var totalSw = Stopwatch.StartNew();
        var auditId = Guid.NewGuid().ToString("N")[..16];
        var allViolations = new List<Violation>();

        // --- Stage 1: Deterministic Checks (<50ms target) ---
        var detSw = Stopwatch.StartNew();
        var deterministicResults = _deterministic.Execute(request.Content, request.Source);
        detSw.Stop();
        allViolations.AddRange(deterministicResults);

        // Fast-path: if critical violation found deterministically, block immediately
        if (deterministicResults.Any(v => v.Severity == "critical" && v.Confidence >= 0.95))
        {
            totalSw.Stop();
            return new DetectionResult(
                Decision: "block",
                Confidence: 1.0,
                Violations: allViolations,
                Timings: new StageTimings(detSw.ElapsedMilliseconds, 0, 0, totalSw.ElapsedMilliseconds),
                AuditId: auditId);
        }

        // --- Stage 2: OPA Policy Check (cached) ---
        var ensembleSw = Stopwatch.StartNew();
        var opaViolations = await CheckOpaPoliciesCachedAsync(request);
        allViolations.AddRange(opaViolations);
        ensembleSw.Stop();

        // --- Stage 3: LLM-as-Judge (async, circuit-breaker protected) ---
        long llmMs = 0;
        if (ShouldInvokeLlm(allViolations, request))
        {
            var llmSw = Stopwatch.StartNew();
            var llmResult = await InvokeLlmJudgeAsync(request);
            llmSw.Stop();
            llmMs = llmSw.ElapsedMilliseconds;
            if (llmResult != null)
                allViolations.AddRange(llmResult);
        }

        // --- Stage 4: Ensemble Decision ---
        var (decision, confidence) = _ensemble.Decide(allViolations, request.Frameworks);

        totalSw.Stop();
        return new DetectionResult(
            Decision: decision,
            Confidence: confidence,
            Violations: allViolations,
            Timings: new StageTimings(detSw.ElapsedMilliseconds, ensembleSw.ElapsedMilliseconds, llmMs, totalSw.ElapsedMilliseconds),
            AuditId: auditId);
    }

    private async Task<List<Violation>> CheckOpaPoliciesCachedAsync(DetectionRequest request)
    {
        var db = _redis.GetDatabase();
        var cacheKey = $"opa:{ComputeHash(request.Content)}:{string.Join(",", request.Frameworks)}";

        var cached = await db.StringGetAsync(cacheKey);
        if (cached.HasValue)
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<Violation>>(cached!) ?? [];
        }

        var violations = new List<Violation>();
        var client = _httpClientFactory.CreateClient("AiService");

        try
        {
            var response = await client.PostAsJsonAsync("/api/policies/evaluate", new
            {
                content = request.Content,
                resource_type = request.ResourceType,
                frameworks = request.Frameworks,
            });

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OpaEvalResult>();
                if (result?.Violations != null)
                {
                    violations.AddRange(result.Violations.Select(v => new Violation(
                        Rule: v.Rule ?? "opa",
                        Type: v.Type ?? "policy",
                        Severity: v.Severity ?? "medium",
                        Message: v.Message ?? "",
                        Remediation: v.Remediation ?? "escalate",
                        Stage: "opa",
                        Confidence: 0.9)));
                }
            }
        }
        catch (HttpRequestException) { }

        await db.StringSetAsync(cacheKey,
            System.Text.Json.JsonSerializer.Serialize(violations),
            TimeSpan.FromMinutes(5));

        return violations;
    }

    private async Task<List<Violation>?> InvokeLlmJudgeAsync(DetectionRequest request)
    {
        if (!_llmCircuitBreaker.AllowRequest())
            return null;

        try
        {
            var client = _httpClientFactory.CreateClient("AiService");
            var response = await client.PostAsJsonAsync("/api/llm/judge", new
            {
                content = request.Content,
                resource_type = request.ResourceType,
                frameworks = request.Frameworks,
                existing_violations = request.Context?.GetValueOrDefault("existing_violations"),
            });

            if (response.IsSuccessStatusCode)
            {
                _llmCircuitBreaker.RecordSuccess();
                var result = await response.Content.ReadFromJsonAsync<LlmJudgeResult>();
                return result?.Violations?.Select(v => new Violation(
                    Rule: v.Rule ?? "llm-judge",
                    Type: v.Type ?? "semantic",
                    Severity: v.Severity ?? "medium",
                    Message: v.Message ?? "",
                    Remediation: v.Remediation ?? "escalate",
                    Stage: "llm-judge",
                    Confidence: v.Confidence)).ToList();
            }

            _llmCircuitBreaker.RecordFailure();
            return null;
        }
        catch
        {
            _llmCircuitBreaker.RecordFailure();
            return null;
        }
    }

    private static bool ShouldInvokeLlm(List<Violation> current, DetectionRequest request)
    {
        if (current.Any(v => v.Severity == "critical" && v.Confidence >= 0.95))
            return false;

        if (current.Count == 0 && request.Frameworks.Contains("INTERNAL"))
            return true;

        if (current.Any(v => v.Confidence < 0.7))
            return true;

        return current.Count > 0 && current.All(v => v.Confidence < 0.85);
    }

    private static string ComputeHash(string input)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..16];
    }
}

record OpaEvalResult(List<OpaViolation>? Violations, string? Decision);
record OpaViolation(string? Rule, string? Type, string? Severity, string? Message, string? Remediation);
record LlmJudgeResult(List<LlmViolation>? Violations);
record LlmViolation(string? Rule, string? Type, string? Severity, string? Message, string? Remediation, double Confidence);
