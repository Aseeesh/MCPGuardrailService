using Microsoft.AspNetCore.Mvc;
using GuardrailApi.Performance.Caching;
using GuardrailApi.Performance.Monitoring;

namespace GuardrailApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MetricsController : ControllerBase
{
    private readonly PerformanceTracker _tracker;
    private readonly PolicyCache _cache;

    public MetricsController(PerformanceTracker tracker, PolicyCache cache)
    {
        _tracker = tracker;
        _cache = cache;
    }

    [HttpGet]
    public IActionResult GetMetrics()
    {
        var cacheStats = _cache.GetStats();
        return Ok(_tracker.GetMetrics(cacheStats));
    }

    [HttpGet("latency/{stage}")]
    public IActionResult GetLatency(string stage) => Ok(_tracker.GetLatency(stage));

    [HttpGet("cache")]
    public IActionResult GetCacheStats() => Ok(_cache.GetStats());

    [HttpGet("prometheus")]
    [Produces("text/plain")]
    public IActionResult GetPrometheus() => Content(_tracker.GetPrometheusMetrics(), "text/plain");

    [HttpPost("reset")]
    public IActionResult Reset()
    {
        _tracker.Reset();
        _cache.ResetStats();
        return Ok(new { Status = "reset" });
    }
}
