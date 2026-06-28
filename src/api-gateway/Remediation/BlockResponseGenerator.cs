namespace GuardrailApi.Remediation;

public record BlockResponse(
    string Status,
    string Category,
    string UserMessage,
    string InternalReason,
    string[] ViolationIds,
    string AuditId,
    DateTime Timestamp,
    BlockAction? SuggestedAction);

public record BlockAction(string Type, string Description, string? ContactUrl);

public class BlockResponseGenerator
{
    private static readonly Dictionary<string, BlockTemplate> Templates = new()
    {
        ["policy"] = new(
            "Policy Violation",
            "Your request could not be processed because it conflicts with our content policies. " +
            "Please review and modify your input to comply with our guidelines.",
            new BlockAction("modify", "Review our content policy and adjust your request", "/docs/content-policy")),

        ["safety"] = new(
            "Safety Concern",
            "Your request was blocked because it may involve content that could cause harm. " +
            "If you believe this is an error, please contact our support team.",
            new BlockAction("contact", "Contact support if you believe this is an error", "/support")),

        ["compliance"] = new(
            "Compliance Requirement",
            "Your request contains information that cannot be processed due to regulatory requirements " +
            "(such as GDPR, HIPAA, or SOC2). Please remove sensitive data and try again.",
            new BlockAction("redact", "Remove sensitive data from your request and retry", null)),

        ["security"] = new(
            "Security Alert",
            "Your request was blocked because it contains patterns associated with security threats. " +
            "If this is legitimate content, please contact your administrator.",
            new BlockAction("contact", "Contact your administrator for assistance", "/admin")),

        ["credential"] = new(
            "Credential Exposure",
            "Your request was blocked because it contains credentials or secrets that should not be shared. " +
            "Please remove all passwords, API keys, and tokens before resubmitting.",
            new BlockAction("redact", "Remove credentials and secrets from your request", null)),

        ["pii"] = new(
            "Personal Data Protection",
            "Your request contains personal information that cannot be processed in this context. " +
            "Please remove personally identifiable information and try again.",
            new BlockAction("redact", "Remove personal data (emails, phone numbers, SSNs, etc.)", null)),
    };

    public BlockResponse Generate(
        string category,
        string internalReason,
        string[] violationIds,
        string auditId)
    {
        var template = Templates.GetValueOrDefault(category, Templates["policy"]);

        return new BlockResponse(
            Status: "blocked",
            Category: template.Category,
            UserMessage: template.UserMessage,
            InternalReason: internalReason,
            ViolationIds: violationIds,
            AuditId: auditId,
            Timestamp: DateTime.UtcNow,
            SuggestedAction: template.Action);
    }

    public BlockResponse GenerateFromViolations(
        List<Pipeline.Violation> violations,
        string auditId)
    {
        var category = DetermineCategory(violations);
        var reason = string.Join("; ", violations.Select(v => v.Message).Distinct().Take(3));
        var ids = violations.Select(v => v.Rule).Distinct().ToArray();

        return Generate(category, reason, ids, auditId);
    }

    private static string DetermineCategory(List<Pipeline.Violation> violations)
    {
        var types = violations.Select(v => v.Type).ToHashSet();

        if (types.Any(t => t.Contains("injection") || t.Contains("xss") || t.Contains("command")))
            return "security";
        if (types.Any(t => t.Contains("credential") || t.Contains("secret") || t.Contains("key")))
            return "credential";
        if (types.Any(t => t.Contains("pii") || t.Contains("phi") || t.Contains("ssn")))
            return "pii";
        if (types.Any(t => t.Contains("safety") || t.Contains("toxic") || t.Contains("harm")))
            return "safety";
        if (types.Any(t => t.Contains("compliance") || t.Contains("gdpr") || t.Contains("hipaa")))
            return "compliance";
        return "policy";
    }
}

record BlockTemplate(string Category, string UserMessage, BlockAction? Action);
