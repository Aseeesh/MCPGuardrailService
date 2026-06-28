using GuardrailApi.Pipeline;

namespace GuardrailApi.Remediation;

public record RemediationResponse(
    string Action,
    string OriginalDecision,
    double Confidence,
    string? RedactedContent,
    RedactionStats? RedactionStats,
    BlockResponse? BlockResponse,
    EscalationTicket? EscalationTicket,
    string AuditId);

public record EscalationTicket(
    string TicketId,
    string Priority,
    string Status,
    string Reason,
    string ContentPreview,
    List<Violation> Violations,
    DateTime CreatedAt);

public class RemediationService
{
    private readonly RedactionEngine _redactionEngine = new();
    private readonly BlockResponseGenerator _blockGenerator = new();
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly double _autoApproveThreshold;
    private readonly double _escalationThreshold;

    public RemediationService(
        IHttpClientFactory httpClientFactory,
        double autoApproveThreshold = 0.85,
        double escalationThreshold = 0.7)
    {
        _httpClientFactory = httpClientFactory;
        _autoApproveThreshold = autoApproveThreshold;
        _escalationThreshold = escalationThreshold;
    }

    public async Task<RemediationResponse> RemediateAsync(DetectionResult detection, string content)
    {
        // Auto-approve high-confidence clean content
        if (detection.Decision == "allow" && detection.Confidence >= _autoApproveThreshold)
        {
            return new RemediationResponse(
                Action: "allow",
                OriginalDecision: detection.Decision,
                Confidence: detection.Confidence,
                RedactedContent: null,
                RedactionStats: null,
                BlockResponse: null,
                EscalationTicket: null,
                AuditId: detection.AuditId);
        }

        return detection.Decision switch
        {
            "redact" => HandleRedaction(detection, content),
            "block" => HandleBlock(detection),
            "rewrite" => await HandleRewrite(detection, content),
            "escalate" => await HandleEscalation(detection, content),
            _ when detection.Confidence < _escalationThreshold =>
                await HandleEscalation(detection, content),
            _ => HandleBlock(detection),
        };
    }

    private RemediationResponse HandleRedaction(DetectionResult detection, string content)
    {
        var typesToRedact = detection.Violations
            .Where(v => v.Remediation == "redact")
            .Select(v => v.Type)
            .Distinct()
            .ToList();

        var redactionResult = typesToRedact.Count > 0
            ? _redactionEngine.RedactSelective(content, MapViolationTypesToRedactionTypes(typesToRedact))
            : _redactionEngine.Redact(content);

        return new RemediationResponse(
            Action: "redact",
            OriginalDecision: detection.Decision,
            Confidence: detection.Confidence,
            RedactedContent: redactionResult.RedactedContent,
            RedactionStats: redactionResult.Stats,
            BlockResponse: null,
            EscalationTicket: null,
            AuditId: detection.AuditId);
    }

    private RemediationResponse HandleBlock(DetectionResult detection)
    {
        var blockResponse = _blockGenerator.GenerateFromViolations(detection.Violations, detection.AuditId);

        return new RemediationResponse(
            Action: "block",
            OriginalDecision: detection.Decision,
            Confidence: detection.Confidence,
            RedactedContent: null,
            RedactionStats: null,
            BlockResponse: blockResponse,
            EscalationTicket: null,
            AuditId: detection.AuditId);
    }

    private async Task<RemediationResponse> HandleRewrite(DetectionResult detection, string content)
    {
        var client = _httpClientFactory.CreateClient("AiService");
        try
        {
            var response = await client.PostAsJsonAsync("/api/remediate", new
            {
                content,
                resource_type = "text",
                action = "rewrite",
            });

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<RewriteResult>();
                return new RemediationResponse(
                    Action: "rewrite",
                    OriginalDecision: detection.Decision,
                    Confidence: detection.Confidence,
                    RedactedContent: result?.ModifiedContent ?? content,
                    RedactionStats: null,
                    BlockResponse: null,
                    EscalationTicket: null,
                    AuditId: detection.AuditId);
            }
        }
        catch (HttpRequestException) { }

        return HandleRedaction(detection, content);
    }

    private async Task<RemediationResponse> HandleEscalation(DetectionResult detection, string content)
    {
        var priority = detection.Violations.Any(v => v.Severity == "critical") ? "critical"
            : detection.Violations.Any(v => v.Severity == "high") ? "high"
            : "medium";

        var reason = detection.Confidence < _escalationThreshold
            ? $"Low confidence ({detection.Confidence:F2}) requires human verification"
            : $"Multiple findings ({detection.Violations.Count}) across categories require review";

        var ticket = new EscalationTicket(
            TicketId: $"ESC-{detection.AuditId[..8]}",
            Priority: priority,
            Status: "pending",
            Reason: reason,
            ContentPreview: content.Length > 300 ? content[..300] + "..." : content,
            Violations: detection.Violations,
            CreatedAt: DateTime.UtcNow);

        // Submit to review queue
        var client = _httpClientFactory.CreateClient("AiService");
        try
        {
            await client.PostAsJsonAsync("/api/review/submit", new
            {
                content,
                decision = detection.Decision,
                confidence = detection.Confidence,
                violations = detection.Violations.Select(v => new
                {
                    v.Rule, v.Type, v.Severity, v.Message, v.Confidence,
                }),
                reason,
            });
        }
        catch (HttpRequestException) { }

        return new RemediationResponse(
            Action: "escalate",
            OriginalDecision: detection.Decision,
            Confidence: detection.Confidence,
            RedactedContent: null,
            RedactionStats: null,
            BlockResponse: null,
            EscalationTicket: ticket,
            AuditId: detection.AuditId);
    }

    private static List<string> MapViolationTypesToRedactionTypes(List<string> violationTypes)
    {
        var map = new Dictionary<string, string>
        {
            ["pii_email"] = "email",
            ["pii_phone"] = "phone",
            ["pii_ssn"] = "ssn",
            ["pii_credit_card"] = "credit_card",
            ["pii_iban"] = "iban",
            ["pii_ip_address"] = "ip_address",
            ["pii_date_of_birth"] = "dob",
            ["pii_detected"] = "email",
            ["ssn_detected"] = "ssn",
            ["mrn_detected"] = "mrn",
            ["credential_exposure"] = "generic_secret",
            ["aws_key"] = "aws_key",
            ["cloud_credential"] = "aws_key",
            ["jwt_token"] = "jwt",
        };

        return violationTypes
            .Select(t => map.GetValueOrDefault(t, t))
            .Distinct()
            .ToList();
    }
}

record RewriteResult(string Action, string ModifiedContent, string Explanation);
