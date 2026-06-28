using Microsoft.AspNetCore.Mvc;
using GuardrailApi.Compliance.Controls;
using GuardrailApi.Compliance.Evidence;

namespace GuardrailApi.Compliance;

[ApiController]
[Route("api/compliance")]
public class ComplianceAssessmentController : ControllerBase
{
    private readonly EvidenceCollector _evidence = new();

    [HttpGet("assess/{framework}")]
    public IActionResult AssessFramework(string framework, [FromQuery] string tenantId = "default")
    {
        var package = _evidence.CollectEvidence(framework.ToUpper(), tenantId, "last-30-days");
        return Ok(package);
    }

    [HttpGet("assess")]
    public IActionResult AssessAll([FromQuery] string tenantId = "default")
    {
        var packages = _evidence.CollectAllFrameworks(tenantId, "last-30-days");
        var overall = new
        {
            TenantId = tenantId,
            GeneratedAt = DateTime.UtcNow.ToString("O"),
            Frameworks = packages.Select(p => new
            {
                p.Framework,
                p.Summary.TotalControls,
                p.Summary.Compliant,
                p.Summary.Partial,
                p.Summary.NonCompliant,
                p.Summary.OverallScore,
            }),
            OverallScore = packages.Count > 0
                ? Math.Round(packages.Average(p => p.Summary.OverallScore), 1) : 0,
        };
        return Ok(overall);
    }

    [HttpGet("control/{framework}/{controlId}")]
    public IActionResult TestControl(string framework, string controlId, [FromQuery] string tenantId = "default")
    {
        var result = _evidence.RunControlTest(framework.ToUpper(), controlId, tenantId);
        return Ok(result);
    }

    [HttpGet("evidence/{framework}")]
    public IActionResult GetEvidence(string framework, [FromQuery] string tenantId = "default")
    {
        var package = _evidence.CollectEvidence(framework.ToUpper(), tenantId, "last-30-days");
        return Ok(new { package.Framework, package.Artifacts, package.Controls });
    }

    [HttpGet("gaps")]
    public IActionResult GetGaps([FromQuery] string tenantId = "default")
    {
        var packages = _evidence.CollectAllFrameworks(tenantId, "last-30-days");
        var gaps = packages.SelectMany(p => p.Controls
            .Where(c => c.Status is "non_compliant" or "partial" or "not_assessed")
            .Select(c => new
            {
                c.Framework,
                c.ControlId,
                c.ControlName,
                c.Status,
                c.CompliancePct,
                c.RemediationGuidance,
            }))
            .OrderBy(g => g.CompliancePct)
            .ToList();

        return Ok(new { TotalGaps = gaps.Count, Gaps = gaps });
    }
}
