using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GuardrailApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ComplianceController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GuardrailDbContext _db;

    public ComplianceController(IHttpClientFactory httpClientFactory, GuardrailDbContext db)
    {
        _httpClientFactory = httpClientFactory;
        _db = db;
    }

    [HttpPost("scan")]
    public async Task<IActionResult> RunComplianceScan([FromBody] ScanRequest request)
    {
        var client = _httpClientFactory.CreateClient("AiService");
        var response = await client.PostAsJsonAsync("/api/compliance/scan", request);
        var results = await response.Content.ReadFromJsonAsync<List<ComplianceFinding>>();

        if (results != null)
        {
            foreach (var finding in results)
            {
                await _db.ComplianceResults.AddAsync(new ComplianceResult
                {
                    Framework = finding.Framework,
                    ControlId = finding.ControlId,
                    Status = finding.Status,
                    Finding = finding.Description,
                    Remediation = finding.Remediation
                });
            }
            await _db.SaveChangesAsync();
        }

        return Ok(results);
    }

    [HttpGet("results")]
    public async Task<IActionResult> GetResults(
        [FromQuery] string? framework,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var query = _db.ComplianceResults.AsQueryable();

        if (!string.IsNullOrEmpty(framework))
            query = query.Where(r => r.Framework == framework);
        if (!string.IsNullOrEmpty(status))
            query = query.Where(r => r.Status == status);

        var results = await query
            .OrderByDescending(r => r.CheckedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(results);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        var summary = await _db.ComplianceResults
            .GroupBy(r => new { r.Framework, r.Status })
            .Select(g => new { g.Key.Framework, g.Key.Status, Count = g.Count() })
            .ToListAsync();

        return Ok(summary);
    }
}

public record ScanRequest(string[] Frameworks, string? ResourceGroup, string[] ResourceTypes);
public record ComplianceFinding(
    string Framework, string ControlId, string Status,
    string Description, string Remediation, string Severity);
