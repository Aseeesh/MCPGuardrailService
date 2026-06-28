using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GuardrailApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GuardrailController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GuardrailDbContext _db;

    public GuardrailController(IHttpClientFactory httpClientFactory, GuardrailDbContext db)
    {
        _httpClientFactory = httpClientFactory;
        _db = db;
    }

    [HttpPost("validate")]
    public async Task<IActionResult> ValidateContent([FromBody] ValidateRequest request)
    {
        var client = _httpClientFactory.CreateClient("AiService");
        var response = await client.PostAsJsonAsync("/api/validate", request);
        var result = await response.Content.ReadFromJsonAsync<ValidationResult>();

        await _db.AuditEntries.AddAsync(new AuditEntry
        {
            Action = "validate",
            Resource = request.ResourceType,
            Result = result?.Decision ?? "unknown",
            Details = result?.Reasoning
        });
        await _db.SaveChangesAsync();

        return Ok(result);
    }

    [HttpPost("remediate")]
    public async Task<IActionResult> Remediate([FromBody] RemediateRequest request)
    {
        var client = _httpClientFactory.CreateClient("AiService");
        var response = await client.PostAsJsonAsync("/api/remediate", request);
        var result = await response.Content.ReadFromJsonAsync<RemediationResult>();

        await _db.AuditEntries.AddAsync(new AuditEntry
        {
            Action = "remediate",
            Resource = request.ResourceType,
            Result = result?.Action ?? "unknown",
            Details = result?.ModifiedContent
        });
        await _db.SaveChangesAsync();

        return Ok(result);
    }

    [HttpGet("audit")]
    public async Task<IActionResult> GetAuditTrail(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var entries = await _db.AuditEntries
            .OrderByDescending(e => e.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(entries);
    }
}

public record ValidateRequest(string Content, string ResourceType, string[] Frameworks);
public record ValidationResult(string Decision, string Reasoning, string[] Violations);
public record RemediateRequest(string Content, string ResourceType, string Action);
public record RemediationResult(string Action, string ModifiedContent, string Explanation);
