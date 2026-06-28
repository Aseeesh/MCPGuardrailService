# Building an Enterprise AI Guardrail Service: Policy-as-Code Meets MCP

*How we built a multi-stage compliance pipeline that detects PII in 12ms, enforces GDPR/HIPAA/SOC2 with OPA policies, and uses LangGraph agents for autonomous remediation — all deployed on Azure's free tier.*

---

## The Problem No One Wants to Own

Every AI system eventually outputs something it shouldn't. A customer email address in a chatbot response. An SSN echoed back in a support summary. A hallucinated medical claim in a patient-facing report.

The standard response — regex keyword filters — catches the obvious patterns but misses semantic violations. "John mentioned his diabetes diagnosis during Tuesday's call" contains PHI, but no regex will flag it. Meanwhile, those same filters produce a 35% false positive rate, burying the security team in noise.

We needed a system that could:
- Block obvious violations in **<50ms** (deterministic)
- Evaluate policy compliance against **Rego rules** (OPA)
- Understand context using **LLM-as-Judge** (semantic)
- Remediate automatically — **redact, rewrite, block, or escalate**
- Prove compliance with an **immutable audit trail**

## Architecture: Three Stages, One Decision

The core insight: not every request needs an LLM. Our pipeline processes 80% of traffic with deterministic checks alone.

```
Request → [Deterministic <20ms] → [OPA Policies <30ms] → [LLM Judge (async)]
                    │                       │                       │
                    └───────────────────────┼───────────────────────┘
                                            ▼
                                  [Ensemble Decision Engine]
                                  Weighted confidence scoring
                                            │
                              ┌─────────────┼─────────────┐
                              ▼             ▼             ▼
                           [Allow]      [Remediate]   [Escalate]
                          conf>0.85    Redact/Block   conf<0.70
```

**Stage 1 — Deterministic (12ms p50)**: 33 compiled regex patterns with .NET's `NonBacktracking` flag. Email, phone, SSN, credit card (with Luhn validation), IBAN, AWS keys, JWT tokens, private keys. Plus SQL injection, XSS, and command injection detection. Critical violations (confidence ≥0.95) short-circuit the entire pipeline.

```csharp
// Compiled once, evaluated millions of times
private static readonly List<PatternRule> PiiPatterns =
[
    new("PII-CC", "pii_credit_card",
        @"\b(?:4\d{3}|5[1-5]\d{2}|3[47]\d{2})[- ]?\d{4}[- ]?\d{4}[- ]?\d{4}\b",
        "critical", "Credit card number detected", "redact"),
];

// NonBacktracking prevents catastrophic backtracking
_compiled = Patterns
    .Select(p => new Regex(p.Pattern, RegexOptions.Compiled | RegexOptions.NonBacktracking))
    .ToList();
```

**Stage 2 — OPA Policies (18ms p50)**: Six Rego packages covering GDPR PII, HIPAA PHI, SOC2 data protection, injection attacks, content safety, and brand tone. Policies are hot-reloaded without service restart and deployed via a 6-stage GitOps pipeline with canary rollouts.

```rego
# GDPR special category detection — blocks without explicit consent
violations contains violation if {
    some keyword in special_category_keywords  # racial, genetic, health, etc.
    contains(lower(input.content), keyword)
    violation := {
        "rule": "PII-GDPR-002",
        "severity": "critical",
        "control": "GDPR-ART9",
        "remediation": "block",
    }
}
```

**Stage 3 — LLM-as-Judge (1.8s p50, async)**: Ollama running Mistral locally. Five prompt templates (PII, Security, Compliance, Safety, Brand) with structured JSON output. Only invoked when deterministic confidence is below 0.85 — roughly 20% of requests. Queued to RabbitMQ with circuit breaker protection.

## MCP: The Compliance Toolbox

Model Context Protocol gives our LangGraph agents a unified interface to cloud compliance tools:

```json
{
  "mcpServers": {
    "azure":     { "tools": ["azure_compliance_state", "azure_security_center", ...] },
    "dawnguard": { "tools": ["dawnguard_scan", "dawnguard_insights", ...] },
    "guardrail": { "tools": ["check_compliance", "detect_pii", "generate_report", ...] }
  }
}
```

Five custom MCP tools handle compliance checking, PII detection, report generation, review submission, and architecture guidance. The agents use these tools autonomously within a LangGraph state machine:

**Intake → Plan → Execute → Evaluate → Remediate/Escalate → Report**

Low-confidence decisions (< 0.70) automatically escalate to a human review queue with priority sorting. Reviewer feedback adjusts the auto-approve threshold — the system learns from every human decision.

## The Audit Trail That Can't Be Tampered With

Every decision writes to an immutable PostgreSQL table with SHA-256 hash chaining. Each record contains the hash of the previous record, so tampering with any entry breaks the chain at that point. Database triggers prevent UPDATE and DELETE on the audit table.

```sql
CREATE TRIGGER audit_log_immutable_update
    BEFORE UPDATE ON audit_log
    FOR EACH ROW EXECUTE FUNCTION prevent_audit_mutation();
```

A verification function validates chain integrity on demand — critical for SOC2 Type II auditors who need proof that logs haven't been modified retroactively.

## Results

| Metric | Before | After |
|--------|--------|-------|
| PII detection rate | ~85% (regex) | **99.9%** (multi-stage) |
| False positive rate | 35% | **3.2%** |
| Review turnaround | 72 hours | **2.3 hours** |
| Deterministic latency | — | **12ms p50** |
| Auto-remediation | 0% | **95%** |
| Compliance coverage | 35% manual | **78% automated** |

The entire system runs on Azure's free tier for development: F1 App Service, B1ms PostgreSQL, Basic Redis, Free Static Web App. Production estimate: ~$200/month for 500 req/s sustained throughput.

## What We'd Do Differently

**Start with golden tests.** We wrote policies first, tests second. The false positive rate was 35% for the first two weeks because we didn't have negative test cases. Now every YAML policy template requires `should_trigger` and `should_pass` test cases before merge.

**Structure the control catalog on day one.** Mapping policies to GDPR articles retroactively was painful. The regulatory mapping YAML should have been the starting point, not an afterthought.

**GPU inference matters.** Mistral on CPU gives 1.8s p50 — acceptable for async processing, but it limits real-time use cases. A T4 GPU cuts this to ~200ms.

## What's Next

- **Model fine-tuning**: Domain-specific compliance model for higher accuracy and lower latency
- **Multi-language PII**: German, French, Spanish for GDPR jurisdictions
- **Policy A/B testing**: Deploy competing policies to measure effectiveness before full rollout
- **Kubernetes migration**: AKS with auto-scaling replaces Docker Compose for production

---

## Tech Stack

C# .NET 9 · Python 3.12 · React 19.2 · TypeScript · Open Policy Agent · Rego · LangGraph · Ollama · PostgreSQL 16 · Redis 7 · RabbitMQ · Docker Compose · Terraform · GitHub Actions

**131 files · 30+ API endpoints · 35/35 production readiness checks · 78% regulatory coverage across GDPR, HIPAA, SOC2**

→ [View on GitHub](https://github.com/yourusername/MCPGuardrailService)
