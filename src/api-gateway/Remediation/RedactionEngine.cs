using System.Text.RegularExpressions;

namespace GuardrailApi.Remediation;

public record RedactionResult(
    string RedactedContent,
    List<RedactionEntry> Redactions,
    RedactionStats Stats);

public record RedactionEntry(
    string Type,
    string OriginalValue,
    string Replacement,
    int StartIndex,
    int Length,
    string Reason);

public record RedactionStats(
    int TotalRedactions,
    int PiiRedactions,
    int CredentialRedactions,
    int CustomRedactions,
    double ContentPreservationPct);

public class RedactionEngine
{
    private static readonly List<RedactionPattern> Patterns =
    [
        // PII
        new("email", @"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}", "[EMAIL_REDACTED]", "PII: Email address"),
        new("phone", @"(?:\+?1[-.\s]?)?\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}", "[PHONE_REDACTED]", "PII: Phone number"),
        new("ssn", @"\b\d{3}-\d{2}-\d{4}\b", "[SSN_REDACTED]", "PII: Social Security Number"),
        new("credit_card", @"\b(?:4\d{3}|5[1-5]\d{2}|3[47]\d{2}|6(?:011|5\d{2}))[- ]?\d{4}[- ]?\d{4}[- ]?\d{4}\b", "[CC_REDACTED]", "PII: Credit card number"),
        new("iban", @"\b[A-Z]{2}\d{2}[A-Z0-9]{4}\d{7}([A-Z0-9]?){0,16}\b", "[IBAN_REDACTED]", "PII: IBAN"),
        new("ip_address", @"\b(?:(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\.){3}(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\b", "[IP_REDACTED]", "PII: IP address"),
        new("dob", @"\b(?:0[1-9]|1[0-2])[/\-](?:0[1-9]|[12]\d|3[01])[/\-](?:19|20)\d{2}\b", "[DOB_REDACTED]", "PII: Date of birth"),
        new("mrn", @"MRN[-:\s]?[A-Z0-9]{6,12}", "[MRN_REDACTED]", "PHI: Medical Record Number"),

        // Credentials
        new("aws_key", @"AKIA[0-9A-Z]{16}", "[AWS_KEY_REDACTED]", "Credential: AWS access key"),
        new("jwt", @"eyJ[A-Za-z0-9_-]+\.eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+", "[JWT_REDACTED]", "Credential: JWT token"),
        new("private_key", @"-----BEGIN (?:RSA |EC |DSA )?PRIVATE KEY-----[\s\S]*?-----END (?:RSA |EC |DSA )?PRIVATE KEY-----", "[PRIVATE_KEY_REDACTED]", "Credential: Private key"),
        new("generic_secret", @"(?i)(?:password|secret|api[_-]?key|token)\s*[:=]\s*['""]?([A-Za-z0-9+/=_\-]{8,})['""]?", "[SECRET_REDACTED]", "Credential: Generic secret"),
    ];

    private readonly List<Regex> _compiled;

    public RedactionEngine()
    {
        _compiled = Patterns
            .Select(p => new Regex(p.Pattern, RegexOptions.Compiled))
            .ToList();
    }

    public RedactionResult Redact(string content, RedactionMode mode = RedactionMode.Standard)
    {
        var redactions = new List<RedactionEntry>();
        var result = content;
        int offset = 0;

        for (int i = 0; i < Patterns.Count; i++)
        {
            var matches = _compiled[i].Matches(result);
            foreach (Match match in matches.Cast<Match>().Reverse())
            {
                var pattern = Patterns[i];
                var replacement = mode switch
                {
                    RedactionMode.Full => "[REDACTED]",
                    RedactionMode.Partial => GetPartialRedaction(pattern.Type, match.Value),
                    _ => pattern.Replacement,
                };

                redactions.Add(new RedactionEntry(
                    Type: pattern.Type,
                    OriginalValue: MaskForAudit(pattern.Type, match.Value),
                    Replacement: replacement,
                    StartIndex: match.Index,
                    Length: match.Length,
                    Reason: pattern.Reason));

                result = result[..match.Index] + replacement + result[(match.Index + match.Length)..];
            }
        }

        redactions.Reverse();

        int pii = redactions.Count(r => r.Reason.StartsWith("PII") || r.Reason.StartsWith("PHI"));
        int cred = redactions.Count(r => r.Reason.StartsWith("Credential"));
        int custom = redactions.Count - pii - cred;
        double preserved = content.Length > 0
            ? Math.Round((double)result.Length / content.Length * 100, 1)
            : 100.0;

        return new RedactionResult(
            RedactedContent: result,
            Redactions: redactions,
            Stats: new RedactionStats(redactions.Count, pii, cred, custom, preserved));
    }

    public RedactionResult RedactSelective(string content, List<string> typesToRedact)
    {
        var redactions = new List<RedactionEntry>();
        var result = content;

        for (int i = 0; i < Patterns.Count; i++)
        {
            if (!typesToRedact.Contains(Patterns[i].Type))
                continue;

            var matches = _compiled[i].Matches(result);
            foreach (Match match in matches.Cast<Match>().Reverse())
            {
                var pattern = Patterns[i];
                redactions.Add(new RedactionEntry(
                    pattern.Type, MaskForAudit(pattern.Type, match.Value),
                    pattern.Replacement, match.Index, match.Length, pattern.Reason));
                result = result[..match.Index] + pattern.Replacement + result[(match.Index + match.Length)..];
            }
        }

        redactions.Reverse();
        int pii = redactions.Count(r => r.Reason.StartsWith("PII") || r.Reason.StartsWith("PHI"));
        int cred = redactions.Count(r => r.Reason.StartsWith("Credential"));
        double preserved = content.Length > 0 ? Math.Round((double)result.Length / content.Length * 100, 1) : 100;

        return new RedactionResult(result, redactions,
            new RedactionStats(redactions.Count, pii, cred, redactions.Count - pii - cred, preserved));
    }

    private static string GetPartialRedaction(string type, string value) => type switch
    {
        "email" => PartialEmail(value),
        "phone" => value.Length > 4 ? $"***-***-{value[^4..]}" : "[PHONE_REDACTED]",
        "ssn" => $"***-**-{value[^4..]}",
        "credit_card" => $"****-****-****-{new string(value.Where(char.IsDigit).TakeLast(4).ToArray())}",
        _ => "[REDACTED]",
    };

    private static string PartialEmail(string email)
    {
        var parts = email.Split('@');
        if (parts.Length != 2) return "[EMAIL_REDACTED]";
        var local = parts[0];
        var masked = local.Length > 2 ? $"{local[0]}***{local[^1]}" : "***";
        return $"{masked}@{parts[1]}";
    }

    private static string MaskForAudit(string type, string value) => type switch
    {
        "ssn" => $"***-**-{value[^4..]}",
        "credit_card" => $"****{new string(value.Where(char.IsDigit).TakeLast(4).ToArray())}",
        "private_key" => "-----BEGIN PRIVATE KEY----- [MASKED] -----END PRIVATE KEY-----",
        "jwt" => $"{value[..10]}...[MASKED]",
        _ when value.Length > 6 => $"{value[..3]}***{value[^3..]}",
        _ => "***",
    };
}

public enum RedactionMode
{
    Standard,
    Full,
    Partial,
}

record RedactionPattern(string Type, string Pattern, string Replacement, string Reason);
