using System.Text.RegularExpressions;

namespace GuardrailApi.Pipeline;

public class DeterministicStage
{
    private static readonly List<PatternRule> PiiPatterns =
    [
        new("PII-EMAIL", "pii_email", @"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}", "high", "Email address detected", "redact"),
        new("PII-PHONE", "pii_phone", @"(?:\+?1[-.\s]?)?\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}", "high", "Phone number detected", "redact"),
        new("PII-CC", "pii_credit_card", @"\b(?:4\d{3}|5[1-5]\d{2}|3[47]\d{2}|6(?:011|5\d{2}))[- ]?\d{4}[- ]?\d{4}[- ]?\d{4}\b", "critical", "Credit card number detected", "redact"),
        new("PII-SSN", "pii_ssn", @"\b\d{3}-\d{2}-\d{4}\b", "critical", "SSN detected", "redact"),
        new("PII-IBAN", "pii_iban", @"\b[A-Z]{2}\d{2}[A-Z0-9]{4}\d{7}([A-Z0-9]?){0,16}\b", "high", "IBAN detected", "redact"),
        new("PII-IP", "pii_ip_address", @"\b(?:(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\.){3}(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\b", "medium", "IP address detected", "redact"),
        new("PII-DOB", "pii_date_of_birth", @"\b(?:0[1-9]|1[0-2])[/\-](?:0[1-9]|[12]\d|3[01])[/\-](?:19|20)\d{2}\b", "high", "Date of birth pattern detected", "redact"),
    ];

    private static readonly List<PatternRule> SecurityPatterns =
    [
        new("SEC-SQLI", "sql_injection", @"(?i)(?:union\s+(?:all\s+)?select|;\s*(?:drop|delete|update|insert|alter)\s|(?:or|and)\s+1\s*=\s*1|exec\s*\(|xp_cmdshell)", "critical", "SQL injection pattern detected", "block"),
        new("SEC-XSS", "xss", @"(?i)(?:<script[^>]*>|javascript\s*:|on(?:error|load|click|mouseover)\s*=|<iframe|<embed|<object)", "critical", "XSS pattern detected", "block"),
        new("SEC-CMDI", "command_injection", @"(?:[;&|]\s*(?:cat|ls|pwd|whoami|id|curl|wget)\b|\$\([^)]+\)|;\s*rm\s+-rf?\s|\.\.\/\.\.\/)", "critical", "Command injection pattern detected", "block"),
        new("SEC-PATH", "path_traversal", @"(?:\.\.[\\/]){2,}", "high", "Path traversal attempt detected", "block"),
        new("SEC-TMPL", "template_injection", @"(?:\{\{.*\}\}|\{%.*%\}|\$\{[^}]+\})", "high", "Template injection pattern detected", "block"),
    ];

    private static readonly List<PatternRule> CredentialPatterns =
    [
        new("SEC-CRED-AWS", "aws_key", @"AKIA[0-9A-Z]{16}", "critical", "AWS access key detected", "block"),
        new("SEC-CRED-GENERIC", "generic_secret", @"(?i)(?:password|secret|api[_-]?key|token|bearer)\s*[:=]\s*['""]?[A-Za-z0-9+/=_\-]{8,}['""]?", "critical", "Credential/secret detected", "redact"),
        new("SEC-CRED-JWT", "jwt_token", @"eyJ[A-Za-z0-9_-]+\.eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+", "critical", "JWT token detected", "redact"),
        new("SEC-CRED-PRIVATE", "private_key", @"-----BEGIN (?:RSA |EC |DSA )?PRIVATE KEY-----", "critical", "Private key detected", "block"),
    ];

    private static readonly HashSet<string> ForbiddenTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "confidential", "top secret", "classified",
        "internal only", "do not distribute", "restricted",
    };

    private readonly List<Regex> _compiledPii;
    private readonly List<Regex> _compiledSecurity;
    private readonly List<Regex> _compiledCredentials;

    public DeterministicStage()
    {
        var options = RegexOptions.Compiled | RegexOptions.NonBacktracking;
        _compiledPii = PiiPatterns.Select(p => new Regex(p.Pattern, options)).ToList();
        _compiledSecurity = SecurityPatterns.Select(p => new Regex(p.Pattern, options)).ToList();
        _compiledCredentials = CredentialPatterns.Select(p => new Regex(p.Pattern, options)).ToList();
    }

    public List<Violation> Execute(string content, string source)
    {
        var violations = new List<Violation>();

        // PII Detection
        for (int i = 0; i < PiiPatterns.Count; i++)
        {
            if (_compiledPii[i].IsMatch(content))
            {
                var rule = PiiPatterns[i];
                violations.Add(new Violation(
                    rule.Id, rule.Type, rule.Severity, rule.Message,
                    rule.Remediation, "deterministic", 0.95));
            }
        }

        // Security Checks (only on user input)
        if (source is "user_input" or "api_request")
        {
            for (int i = 0; i < SecurityPatterns.Count; i++)
            {
                if (_compiledSecurity[i].IsMatch(content))
                {
                    var rule = SecurityPatterns[i];
                    violations.Add(new Violation(
                        rule.Id, rule.Type, rule.Severity, rule.Message,
                        rule.Remediation, "deterministic", 0.98));
                }
            }
        }

        // Credential Detection
        for (int i = 0; i < CredentialPatterns.Count; i++)
        {
            if (_compiledCredentials[i].IsMatch(content))
            {
                var rule = CredentialPatterns[i];
                violations.Add(new Violation(
                    rule.Id, rule.Type, rule.Severity, rule.Message,
                    rule.Remediation, "deterministic", 0.99));
            }
        }

        // Forbidden Terms
        foreach (var term in ForbiddenTerms)
        {
            if (content.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(new Violation(
                    "DET-FORBIDDEN", "forbidden_term", "medium",
                    $"Forbidden term detected: '{term}'",
                    "escalate", "deterministic", 0.90));
            }
        }

        // Length Validation
        if (content.Length > 100_000)
        {
            violations.Add(new Violation(
                "DET-LENGTH", "content_too_long", "low",
                "Content exceeds maximum allowed length (100KB)",
                "block", "deterministic", 1.0));
        }

        // Schema Validation for JSON
        if (content.TrimStart().StartsWith('{') || content.TrimStart().StartsWith('['))
        {
            try { System.Text.Json.JsonDocument.Parse(content); }
            catch (System.Text.Json.JsonException)
            {
                violations.Add(new Violation(
                    "DET-SCHEMA", "invalid_json", "low",
                    "Content appears to be JSON but is malformed",
                    "block", "deterministic", 1.0));
            }
        }

        // Luhn check for credit card candidates
        violations = violations
            .Select(v => v.Type == "pii_credit_card" && !LuhnCheck(ExtractDigits(content))
                ? v with { Confidence = 0.3, Message = "Possible credit card (failed Luhn)" }
                : v)
            .ToList();

        return violations;
    }

    private static string ExtractDigits(string input)
    {
        return new string(input.Where(char.IsDigit).ToArray());
    }

    private static bool LuhnCheck(string digits)
    {
        if (digits.Length < 13 || digits.Length > 19) return false;
        int sum = 0;
        bool alternate = false;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int n = digits[i] - '0';
            if (alternate)
            {
                n *= 2;
                if (n > 9) n -= 9;
            }
            sum += n;
            alternate = !alternate;
        }
        return sum % 10 == 0;
    }
}

record PatternRule(string Id, string Type, string Pattern, string Severity, string Message, string Remediation);
