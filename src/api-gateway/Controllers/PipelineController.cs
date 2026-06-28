using Microsoft.AspNetCore.Mvc;
using GuardrailApi.Pipeline;

namespace GuardrailApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PipelineController : ControllerBase
{
    private readonly DetectionPipeline _pipeline;
    private readonly GuardrailDbContext _db;

    public PipelineController(
        DetectionPipeline pipeline,
        GuardrailDbContext db)
    {
        _pipeline = pipeline;
        _db = db;
    }

    [HttpPost("detect")]
    public async Task<IActionResult> Detect([FromBody] PipelineRequest request)
    {
        var result = await _pipeline.ExecuteAsync(new DetectionRequest(
            Content: request.Content,
            ResourceType: request.ResourceType ?? "text",
            Source: request.Source ?? "user_input",
            Frameworks: request.Frameworks ?? ["GDPR", "HIPAA", "SOC2"],
            Context: null));

        await _db.AuditEntries.AddAsync(new AuditEntry
        {
            Action = "pipeline_detect",
            Resource = request.ResourceType ?? "text",
            Result = result.Decision,
            Details = System.Text.Json.JsonSerializer.Serialize(new
            {
                result.AuditId,
                result.Confidence,
                ViolationCount = result.Violations.Count,
                result.Timings,
            }),
        });
        await _db.SaveChangesAsync();

        return Ok(result);
    }

    [HttpPost("detect/batch")]
    public async Task<IActionResult> DetectBatch([FromBody] BatchPipelineRequest request)
    {
        var tasks = request.Items.Select(item => _pipeline.ExecuteAsync(new DetectionRequest(
            Content: item.Content,
            ResourceType: item.ResourceType ?? "text",
            Source: item.Source ?? "user_input",
            Frameworks: request.Frameworks ?? ["GDPR", "HIPAA", "SOC2"],
            Context: null)));

        var results = await Task.WhenAll(tasks);
        return Ok(new { Results = results, Total = results.Length });
    }
}

public record PipelineRequest(
    string Content,
    string? ResourceType,
    string? Source,
    string[]? Frameworks);

public record BatchPipelineRequest(
    List<BatchItem> Items,
    string[]? Frameworks);

public record BatchItem(
    string Content,
    string? ResourceType,
    string? Source);
