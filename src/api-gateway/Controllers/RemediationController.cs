using Microsoft.AspNetCore.Mvc;
using GuardrailApi.Pipeline;
using GuardrailApi.Remediation;

namespace GuardrailApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RemediationController : ControllerBase
{
    private readonly DetectionPipeline _pipeline;
    private readonly RemediationService _remediation;
    private readonly GuardrailDbContext _db;

    public RemediationController(
        DetectionPipeline pipeline,
        RemediationService remediation,
        GuardrailDbContext db)
    {
        _pipeline = pipeline;
        _remediation = remediation;
        _db = db;
    }

    [HttpPost("process")]
    public async Task<IActionResult> Process([FromBody] ProcessRequest request)
    {
        var detection = await _pipeline.ExecuteAsync(new DetectionRequest(
            Content: request.Content,
            ResourceType: request.ResourceType ?? "text",
            Source: request.Source ?? "user_input",
            Frameworks: request.Frameworks ?? ["GDPR", "HIPAA", "SOC2"]));

        var result = await _remediation.RemediateAsync(detection, request.Content);

        await _db.AuditEntries.AddAsync(new AuditEntry
        {
            Action = $"remediation_{result.Action}",
            Resource = request.ResourceType ?? "text",
            Result = result.Action,
            Details = System.Text.Json.JsonSerializer.Serialize(new
            {
                result.AuditId,
                result.Confidence,
                result.OriginalDecision,
                ViolationCount = detection.Violations.Count,
                detection.Timings,
                EscalationTicketId = result.EscalationTicket?.TicketId,
            }),
        });
        await _db.SaveChangesAsync();

        return Ok(result);
    }

    [HttpPost("redact")]
    public IActionResult Redact([FromBody] RedactRequest request)
    {
        var engine = new RedactionEngine();
        var mode = Enum.TryParse<RedactionMode>(request.Mode, true, out var m) ? m : RedactionMode.Standard;

        var result = request.Types?.Count > 0
            ? engine.RedactSelective(request.Content, request.Types)
            : engine.Redact(request.Content, mode);

        return Ok(result);
    }

    [HttpPost("block-response")]
    public IActionResult GenerateBlockResponse([FromBody] BlockRequest request)
    {
        var generator = new BlockResponseGenerator();
        var response = generator.Generate(
            request.Category,
            request.Reason,
            request.ViolationIds ?? [],
            request.AuditId ?? Guid.NewGuid().ToString("N")[..16]);

        return Ok(response);
    }
}

public record ProcessRequest(
    string Content,
    string? ResourceType,
    string? Source,
    string[]? Frameworks);

public record RedactRequest(
    string Content,
    string? Mode,
    List<string>? Types);

public record BlockRequest(
    string Category,
    string Reason,
    string[]? ViolationIds,
    string? AuditId);
