using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GuardrailApi.Audit;

[ApiController]
[Route("api/[controller]")]
public class AuditController : ControllerBase
{
    private readonly AuditDbContext _db;
    private readonly AuditService _auditService;

    public AuditController(AuditDbContext db, AuditService auditService)
    {
        _db = db;
        _auditService = auditService;
    }

    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs(
        [FromQuery] Guid? tenantId,
        [FromQuery] string? action,
        [FromQuery] string? decision,
        [FromQuery] string? framework,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] bool? humanReviewed,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var query = _db.AuditLogs.AsQueryable();

        if (tenantId.HasValue)
            query = query.Where(l => l.TenantId == tenantId.Value);
        if (!string.IsNullOrEmpty(action))
            query = query.Where(l => l.Action == action);
        if (!string.IsNullOrEmpty(decision))
            query = query.Where(l => l.Decision == decision);
        if (from.HasValue)
            query = query.Where(l => l.Timestamp >= from.Value);
        if (to.HasValue)
            query = query.Where(l => l.Timestamp <= to.Value);
        if (humanReviewed.HasValue)
            query = query.Where(l => l.HumanReviewed == humanReviewed.Value);
        if (!string.IsNullOrEmpty(framework))
            query = query.Where(l => l.Frameworks != null && l.Frameworks.Contains(framework));

        var total = await query.CountAsync();
        var logs = await query
            .OrderByDescending(l => l.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { Total = total, Page = page, PageSize = pageSize, Data = logs });
    }

    [HttpGet("logs/{auditId}")]
    public async Task<IActionResult> GetLog(string auditId)
    {
        var log = await _db.AuditLogs.FirstOrDefaultAsync(l => l.AuditId == auditId);
        if (log == null) return NotFound();

        var violations = await _db.AuditViolations
            .Where(v => v.AuditLogId == log.Id)
            .ToListAsync();

        return Ok(new { Log = log, Violations = violations });
    }

    [HttpGet("logs/{auditId}/verify")]
    public async Task<IActionResult> VerifyIntegrity(string auditId)
    {
        var log = await _db.AuditLogs.FirstOrDefaultAsync(l => l.AuditId == auditId);
        if (log == null) return NotFound();

        var entry = MapToServiceEntry(log);
        var isValid = _auditService.VerifyRecordIntegrity(entry);

        return Ok(new { AuditId = auditId, IntegrityValid = isValid, RecordHash = log.RecordHash });
    }

    [HttpGet("chain/verify")]
    public async Task<IActionResult> VerifyChain(
        [FromQuery] Guid tenantId,
        [FromQuery] int limit = 1000)
    {
        var logs = await _db.AuditLogs
            .Where(l => l.TenantId == tenantId)
            .OrderBy(l => l.Id)
            .Take(limit)
            .ToListAsync();

        var entries = logs.Select(MapToServiceEntry).ToList();
        var isValid = _auditService.VerifyChainIntegrity(entries);

        int? breakAt = null;
        if (!isValid)
        {
            for (int i = 1; i < entries.Count; i++)
            {
                if (entries[i].PreviousHash != entries[i - 1].RecordHash)
                {
                    breakAt = i;
                    break;
                }
            }
        }

        return Ok(new
        {
            TenantId = tenantId,
            RecordsChecked = logs.Count,
            ChainValid = isValid,
            BreakAtIndex = breakAt,
        });
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(
        [FromQuery] Guid? tenantId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
    {
        var query = _db.AuditLogs.AsQueryable();
        if (tenantId.HasValue)
            query = query.Where(l => l.TenantId == tenantId.Value);
        if (from.HasValue)
            query = query.Where(l => l.Timestamp >= from.Value);
        if (to.HasValue)
            query = query.Where(l => l.Timestamp <= to.Value);

        var total = await query.CountAsync();
        var byDecision = await query
            .GroupBy(l => l.Decision)
            .Select(g => new { Decision = g.Key, Count = g.Count() })
            .ToListAsync();
        var byAction = await query
            .GroupBy(l => l.Action)
            .Select(g => new { Action = g.Key, Count = g.Count() })
            .ToListAsync();
        var humanReviewed = await query.CountAsync(l => l.HumanReviewed);
        var avgConfidence = total > 0
            ? await query.AverageAsync(l => l.Confidence)
            : 0;
        var avgPipelineMs = total > 0
            ? await query.Where(l => l.PipelineMs != null).AverageAsync(l => (double)l.PipelineMs!)
            : 0;

        return Ok(new
        {
            Total = total,
            ByDecision = byDecision,
            ByAction = byAction,
            HumanReviewed = humanReviewed,
            AvgConfidence = Math.Round((double)avgConfidence, 4),
            AvgPipelineMs = Math.Round(avgPipelineMs, 1),
        });
    }

    [HttpGet("report")]
    public async Task<IActionResult> GenerateReport(
        [FromQuery] Guid tenantId,
        [FromQuery] string framework,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
    {
        var start = from ?? DateTime.UtcNow.AddDays(-30);
        var end = to ?? DateTime.UtcNow;

        var logs = await _db.AuditLogs
            .Where(l => l.TenantId == tenantId
                && l.Timestamp >= start
                && l.Timestamp <= end
                && l.Frameworks != null
                && l.Frameworks.Contains(framework))
            .ToListAsync();

        var violations = await _db.AuditViolations
            .Where(v => logs.Select(l => l.Id).Contains(v.AuditLogId))
            .ToListAsync();

        var controlsCovered = violations
            .Where(v => v.ControlId != null)
            .Select(v => v.ControlId!)
            .Distinct()
            .ToList();

        return Ok(new
        {
            Framework = framework,
            Period = new { From = start, To = end },
            TotalChecks = logs.Count,
            Decisions = logs.GroupBy(l => l.Decision).Select(g => new { g.Key, Count = g.Count() }),
            TotalViolations = violations.Count,
            BySeverity = violations.GroupBy(v => v.Severity).Select(g => new { g.Key, Count = g.Count() }),
            ControlsCovered = controlsCovered,
            HumanReviews = logs.Count(l => l.HumanReviewed),
            AvgConfidence = logs.Count > 0 ? Math.Round((double)logs.Average(l => l.Confidence), 4) : 0,
        });
    }

    [HttpGet("export")]
    public async Task<IActionResult> ExportAudit(
        [FromQuery] Guid tenantId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string format = "json")
    {
        var start = from ?? DateTime.UtcNow.AddDays(-90);
        var end = to ?? DateTime.UtcNow;

        var logs = await _db.AuditLogs
            .Where(l => l.TenantId == tenantId && l.Timestamp >= start && l.Timestamp <= end)
            .OrderBy(l => l.Timestamp)
            .ToListAsync();

        if (format == "csv")
        {
            var csv = new System.Text.StringBuilder();
            csv.AppendLine("audit_id,timestamp,action,decision,confidence,violation_count,frameworks,human_reviewed,record_hash");
            foreach (var log in logs)
            {
                csv.AppendLine($"{log.AuditId},{log.Timestamp:O},{log.Action},{log.Decision},{log.Confidence},{log.ViolationCount},{string.Join(";", log.Frameworks ?? [])},{log.HumanReviewed},{log.RecordHash}");
            }
            return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"audit_export_{tenantId}_{start:yyyyMMdd}_{end:yyyyMMdd}.csv");
        }

        return Ok(new
        {
            TenantId = tenantId,
            Period = new { From = start, To = end },
            RecordCount = logs.Count,
            Data = logs,
        });
    }

    private static AuditLogEntry MapToServiceEntry(AuditLogEntity entity) => new()
    {
        AuditId = entity.AuditId,
        TenantId = entity.TenantId,
        Timestamp = entity.Timestamp,
        Action = entity.Action,
        ResourceType = entity.ResourceType,
        Source = entity.Source,
        Decision = entity.Decision,
        Confidence = (double)entity.Confidence,
        ViolationCount = entity.ViolationCount,
        InputHash = entity.InputHash,
        PreviousHash = entity.PreviousHash,
        RecordHash = entity.RecordHash,
    };
}
