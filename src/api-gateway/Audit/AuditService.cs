using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GuardrailApi.Pipeline;
using Microsoft.EntityFrameworkCore;

namespace GuardrailApi.Audit;

public record AuditLogEntry
{
    public long Id { get; set; }
    public required string AuditId { get; set; }
    public Guid TenantId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public required string Action { get; set; }
    public required string ResourceType { get; set; }
    public required string Source { get; set; }
    public required string Decision { get; set; }
    public double Confidence { get; set; }
    public int ViolationCount { get; set; }
    public string? PolicyVersion { get; set; }
    public string[]? Frameworks { get; set; }
    public int? PipelineMs { get; set; }
    public required string InputHash { get; set; }
    public string? InputBlobRef { get; set; }
    public string? OutputBlobRef { get; set; }
    public string? RemediationAction { get; set; }
    public int RedactionCount { get; set; }
    public bool HumanReviewed { get; set; }
    public string? ReviewerId { get; set; }
    public string? ReviewDecision { get; set; }
    public string? ReviewNotes { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? PreviousHash { get; set; }
    public required string RecordHash { get; set; }
    public string? Signature { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

public record AuditViolationEntry
{
    public long Id { get; set; }
    public long AuditLogId { get; set; }
    public required string RuleId { get; set; }
    public required string ViolationType { get; set; }
    public required string Severity { get; set; }
    public required string Message { get; set; }
    public string? Remediation { get; set; }
    public double? Confidence { get; set; }
    public string? Stage { get; set; }
    public string? ControlId { get; set; }
    public string? EvidenceHash { get; set; }
}

public class AuditService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private string? _lastRecordHash;
    private readonly object _hashLock = new();

    public AuditService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public AuditLogEntry CreateEntry(
        DetectionResult detection,
        string sanitizedInput,
        Guid tenantId,
        string action,
        string resourceType,
        string source,
        string? remediationAction = null,
        int redactionCount = 0,
        string? policyVersion = null,
        string[]? frameworks = null)
    {
        var inputHash = ComputeSha256(sanitizedInput);

        string previousHash;
        lock (_hashLock)
        {
            previousHash = _lastRecordHash ?? "";
        }

        var entry = new AuditLogEntry
        {
            AuditId = detection.AuditId,
            TenantId = tenantId,
            Action = action,
            ResourceType = resourceType,
            Source = source,
            Decision = detection.Decision,
            Confidence = detection.Confidence,
            ViolationCount = detection.Violations.Count,
            PolicyVersion = policyVersion,
            Frameworks = frameworks,
            PipelineMs = (int)detection.Timings.TotalMs,
            InputHash = inputHash,
            RemediationAction = remediationAction,
            RedactionCount = redactionCount,
            PreviousHash = string.IsNullOrEmpty(previousHash) ? null : previousHash,
            RecordHash = "",
        };

        entry.RecordHash = ComputeRecordHash(entry);

        lock (_hashLock)
        {
            _lastRecordHash = entry.RecordHash;
        }

        return entry;
    }

    public AuditLogEntry AddHumanReview(
        AuditLogEntry entry,
        string reviewerId,
        string reviewDecision,
        string? reviewNotes = null)
    {
        return entry with
        {
            HumanReviewed = true,
            ReviewerId = reviewerId,
            ReviewDecision = reviewDecision,
            ReviewNotes = reviewNotes,
            ReviewedAt = DateTime.UtcNow,
        };
    }

    public List<AuditViolationEntry> CreateViolationEntries(
        long auditLogId,
        List<Violation> violations)
    {
        return violations.Select(v => new AuditViolationEntry
        {
            AuditLogId = auditLogId,
            RuleId = v.Rule,
            ViolationType = v.Type,
            Severity = v.Severity,
            Message = v.Message,
            Remediation = v.Remediation,
            Confidence = v.Confidence,
            Stage = v.Stage,
            EvidenceHash = ComputeSha256(v.Message),
        }).ToList();
    }

    public bool VerifyRecordIntegrity(AuditLogEntry entry)
    {
        var computed = ComputeRecordHash(entry);
        return computed == entry.RecordHash;
    }

    public bool VerifyChainIntegrity(List<AuditLogEntry> entries)
    {
        for (int i = 1; i < entries.Count; i++)
        {
            if (entries[i].PreviousHash != entries[i - 1].RecordHash)
                return false;
            if (!VerifyRecordIntegrity(entries[i]))
                return false;
        }
        return true;
    }

    public async Task<string?> StorePayloadAsync(string content, string containerName, string blobName)
    {
        var client = _httpClientFactory.CreateClient("AzureStorage");
        try
        {
            var response = await client.PutAsync(
                $"/{containerName}/{blobName}",
                new StringContent(content, Encoding.UTF8, "application/json"));
            return response.IsSuccessStatusCode
                ? $"https://storage.blob.core.windows.net/{containerName}/{blobName}"
                : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public string SanitizeInput(string rawInput)
    {
        if (rawInput.Length > 10_000)
            rawInput = rawInput[..10_000] + "...[truncated]";
        return rawInput;
    }

    private static string ComputeRecordHash(AuditLogEntry entry)
    {
        var data = $"{entry.AuditId}|{entry.TenantId}|{entry.Timestamp:O}|" +
                   $"{entry.Action}|{entry.Decision}|{entry.Confidence}|" +
                   $"{entry.InputHash}|{entry.ViolationCount}|" +
                   $"{entry.PreviousHash ?? "null"}";
        return ComputeSha256(data);
    }

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
