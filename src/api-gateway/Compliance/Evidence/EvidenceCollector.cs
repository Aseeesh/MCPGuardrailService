using GuardrailApi.Audit;
using GuardrailApi.Compliance.Controls;

namespace GuardrailApi.Compliance.Evidence;

public record EvidencePackage(
    string Framework,
    string TenantId,
    string GeneratedAt,
    string Period,
    List<ControlStatus> Controls,
    ComplianceSummary Summary,
    List<EvidenceArtifact> Artifacts);

public record ComplianceSummary(
    int TotalControls,
    int Compliant,
    int Partial,
    int NonCompliant,
    int NotAssessed,
    double OverallScore);

public record EvidenceArtifact(
    string Name,
    string Type,       // log, config, report, screenshot, attestation
    string Description,
    string Reference,  // file path or blob URI
    string CollectedAt);

public class EvidenceCollector
{
    private readonly GdprControls _gdpr = new();
    private readonly HipaaControls _hipaa = new();
    private readonly Soc2Controls _soc2 = new();

    public EvidencePackage CollectEvidence(string framework, string tenantId, string period)
    {
        var controls = framework.ToUpper() switch
        {
            "GDPR" => _gdpr.AssessAll(tenantId),
            "HIPAA" => _hipaa.AssessAll(tenantId),
            "SOC2" => _soc2.AssessAll(tenantId),
            _ => [],
        };

        var compliant = controls.Count(c => c.Status == "compliant");
        var partial = controls.Count(c => c.Status == "partial");
        var nonCompliant = controls.Count(c => c.Status == "non_compliant");
        var notAssessed = controls.Count(c => c.Status == "not_assessed");
        var totalScore = controls.Count > 0 ? controls.Average(c => c.CompliancePct) : 0;

        var artifacts = CollectArtifacts(framework);

        return new EvidencePackage(
            Framework: framework,
            TenantId: tenantId,
            GeneratedAt: DateTime.UtcNow.ToString("O"),
            Period: period,
            Controls: controls,
            Summary: new ComplianceSummary(
                controls.Count, compliant, partial, nonCompliant, notAssessed,
                Math.Round(totalScore, 1)),
            Artifacts: artifacts);
    }

    public List<EvidencePackage> CollectAllFrameworks(string tenantId, string period)
    {
        return ["GDPR", "HIPAA", "SOC2"]
            .Select(f => CollectEvidence(f, tenantId, period))
            .ToList();
    }

    public ControlStatus RunControlTest(string framework, string controlId, string tenantId)
    {
        return framework.ToUpper() switch
        {
            "GDPR" => controlId switch
            {
                "ART30" => _gdpr.AssessDataProcessingRecords(tenantId),
                "ART6" => _gdpr.AssessConsentManagement(tenantId),
                "ART17" => _gdpr.AssessRightToErasure(tenantId),
                "ART20" => _gdpr.AssessDataPortability(tenantId),
                "ART33" => _gdpr.AssessBreachNotification(tenantId),
                _ => new ControlStatus(controlId, "Unknown", framework, "not_assessed", 0, null, [], null),
            },
            "HIPAA" => controlId switch
            {
                "164.502" => _hipaa.AssessPhiProtection(tenantId),
                "164.312" => _hipaa.AssessAccessLogs(tenantId),
                "164.312-ENC" => _hipaa.AssessEncryption(tenantId),
                "164.504" => _hipaa.AssessBusinessAssociates(tenantId),
                "164.308" => _hipaa.AssessSecurityIncidents(tenantId),
                _ => new ControlStatus(controlId, "Unknown", framework, "not_assessed", 0, null, [], null),
            },
            "SOC2" => controlId switch
            {
                "CC6.1" => _soc2.AssessSecurity_CC61(tenantId),
                "CC6.2" => _soc2.AssessSecurity_CC62(tenantId),
                "A1.1" => _soc2.AssessAvailability_A11(tenantId),
                "A1.2" => _soc2.AssessAvailability_A12(tenantId),
                "PI1.1" => _soc2.AssessProcessingIntegrity_PI11(tenantId),
                "C1.1" => _soc2.AssessConfidentiality_C11(tenantId),
                "P1.1" => _soc2.AssessPrivacy_P11(tenantId),
                _ => new ControlStatus(controlId, "Unknown", framework, "not_assessed", 0, null, [], null),
            },
            _ => new ControlStatus(controlId, "Unknown", framework, "not_assessed", 0, null, [], null),
        };
    }

    private static List<EvidenceArtifact> CollectArtifacts(string framework)
    {
        var artifacts = new List<EvidenceArtifact>
        {
            new("Audit Trail Schema", "config", "PostgreSQL audit_log schema with immutability triggers",
                "sql/001_audit_schema.sql", DateTime.UtcNow.ToString("O")),
            new("Detection Pipeline Config", "config", "Multi-stage detection pipeline with deterministic, OPA, and LLM stages",
                "src/api-gateway/Pipeline/DetectionPipeline.cs", DateTime.UtcNow.ToString("O")),
            new("OPA Policies", "config", "Rego policies for regulatory compliance",
                "src/policy-engine/policies/", DateTime.UtcNow.ToString("O")),
            new("Hash Chain Verification", "log", "Cryptographic hash chain integrity verification",
                "src/api-gateway/Audit/AuditService.cs", DateTime.UtcNow.ToString("O")),
        };

        if (framework == "GDPR")
        {
            artifacts.Add(new("GDPR PII Policies", "config", "Rego policies for GDPR PII detection",
                "src/policy-engine/policies/pii/gdpr_pii.rego", DateTime.UtcNow.ToString("O")));
            artifacts.Add(new("Redaction Engine", "config", "PII auto-redaction with context preservation",
                "src/api-gateway/Remediation/RedactionEngine.cs", DateTime.UtcNow.ToString("O")));
        }
        else if (framework == "HIPAA")
        {
            artifacts.Add(new("HIPAA PHI Policies", "config", "Rego policies for HIPAA PHI detection",
                "src/policy-engine/policies/pii/hipaa_phi.rego", DateTime.UtcNow.ToString("O")));
        }
        else if (framework == "SOC2")
        {
            artifacts.Add(new("SOC2 Security Policies", "config", "Rego policies for SOC2 data protection",
                "src/policy-engine/policies/pii/soc2_data.rego", DateTime.UtcNow.ToString("O")));
            artifacts.Add(new("Injection Detection", "config", "SQL/XSS/command injection detection",
                "src/policy-engine/policies/security/injection.rego", DateTime.UtcNow.ToString("O")));
        }

        return artifacts;
    }
}
