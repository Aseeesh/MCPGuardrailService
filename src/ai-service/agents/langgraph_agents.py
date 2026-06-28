"""
Autonomous compliance agents built with LangGraph.

Agent Types:
  - PII Detection Agent
  - Policy Violation Agent
  - Remediation Planning Agent
  - Compliance Reporting Agent

Workflow: Intake → Plan → Execute → Evaluate → Remediate → Report
"""

import json
import asyncio
from datetime import datetime, timezone
from typing import TypedDict, Literal
from langgraph.graph import StateGraph, END
import httpx


# --- Shared State ---

class ComplianceState(TypedDict):
    content: str
    resource_type: str
    frameworks: list[str]
    source: str
    # Planning
    checks_required: list[str]
    # Execution
    pii_findings: list[dict]
    policy_findings: list[dict]
    security_findings: list[dict]
    llm_findings: list[dict]
    # Evaluation
    confidence: float
    decision: str
    requires_human_review: bool
    escalation_reason: str
    # Remediation
    remediation_action: str
    remediated_content: str
    # Reporting
    audit_trail: dict
    metrics: dict


# --- PII Detection Agent ---

class PiiDetectionAgent:
    def __init__(self, opa_url: str, ollama_url: str):
        self.opa_url = opa_url
        self.ollama_url = ollama_url

    async def detect(self, state: ComplianceState) -> dict:
        findings = []
        packages = ["pii/gdpr", "pii/hipaa", "pii/soc2"]

        async with httpx.AsyncClient() as client:
            tasks = [
                client.post(
                    f"{self.opa_url}/v1/data/{pkg}/violations",
                    json={"input": {
                        "content": state["content"],
                        "context": {
                            "purpose": "",
                            "retention_days": 0,
                            "cross_border": False,
                            "data_category": "",
                            "hipaa_authorized": False,
                            "encryption": "none",
                            "minimum_necessary": False,
                        },
                    }},
                )
                for pkg in packages
            ]
            responses = await asyncio.gather(*tasks, return_exceptions=True)

            for pkg, resp in zip(packages, responses):
                if isinstance(resp, Exception):
                    continue
                if resp.status_code == 200:
                    violations = resp.json().get("result", [])
                    for v in violations:
                        v["source_policy"] = pkg
                    findings.extend(violations)

        return {"pii_findings": findings}


# --- Policy Violation Agent ---

class PolicyViolationAgent:
    def __init__(self, opa_url: str):
        self.opa_url = opa_url

    async def check(self, state: ComplianceState) -> dict:
        findings = []
        packages = ["security/injection", "safety/content", "brand/tone"]

        async with httpx.AsyncClient() as client:
            for pkg in packages:
                if pkg.split("/")[0] not in state["checks_required"]:
                    continue
                try:
                    resp = await client.post(
                        f"{self.opa_url}/v1/data/{pkg}/violations",
                        json={"input": {
                            "content": state["content"],
                            "context": {
                                "source": state["source"],
                                "authorized_security_research": False,
                                "comparison_approved": False,
                                "content_domain": "",
                            },
                        }},
                    )
                    if resp.status_code == 200:
                        violations = resp.json().get("result", [])
                        for v in violations:
                            v["source_policy"] = pkg
                        findings.extend(violations)
                except httpx.RequestError:
                    continue

        return {"policy_findings": findings}


# --- Remediation Planning Agent ---

class RemediationAgent:
    def __init__(self, ollama_url: str, model: str = "mistral"):
        self.ollama_url = ollama_url
        self.model = model

    async def plan_remediation(self, state: ComplianceState) -> dict:
        if state["decision"] == "allow":
            return {
                "remediation_action": "none",
                "remediated_content": state["content"],
            }

        all_violations = (
            state["pii_findings"]
            + state["policy_findings"]
            + state["llm_findings"]
        )

        actions = [v.get("remediation", "escalate") for v in all_violations]
        if "block" in actions:
            action = "block"
        elif "redact" in actions:
            action = "redact"
        elif "rewrite" in actions:
            action = "rewrite"
        else:
            action = "escalate"

        remediated = state["content"]
        if action == "redact":
            remediated = await self._redact_with_llm(state["content"], all_violations)
        elif action == "rewrite":
            remediated = await self._rewrite_with_llm(state["content"], all_violations)

        return {
            "remediation_action": action,
            "remediated_content": remediated,
        }

    async def _redact_with_llm(self, content: str, violations: list[dict]) -> str:
        prompt = f"""Redact all PII and sensitive information from this content.
Replace each piece of sensitive data with [REDACTED].

Violations found: {json.dumps(violations[:5])}

Content:
{content}

Return ONLY the redacted content, nothing else."""

        return await self._call_llm(prompt) or "[Content redacted pending review]"

    async def _rewrite_with_llm(self, content: str, violations: list[dict]) -> str:
        prompt = f"""Rewrite this content to be compliant while preserving the original intent.
Remove policy violations while keeping the message clear.

Violations: {json.dumps(violations[:5])}

Content:
{content}

Return ONLY the rewritten content, nothing else."""

        return await self._call_llm(prompt) or "[Content pending manual rewrite]"

    async def _call_llm(self, prompt: str) -> str | None:
        async with httpx.AsyncClient() as client:
            try:
                resp = await client.post(
                    f"{self.ollama_url}/api/generate",
                    json={"model": self.model, "prompt": prompt, "stream": False},
                    timeout=60.0,
                )
                return resp.json().get("response")
            except (httpx.RequestError, KeyError):
                return None


# --- Compliance Reporting Agent ---

class ReportingAgent:
    def generate_report(self, state: ComplianceState) -> dict:
        all_violations = (
            state["pii_findings"]
            + state["policy_findings"]
            + state["llm_findings"]
        )

        frameworks_affected = set()
        for v in all_violations:
            if "control" in v:
                ctrl = v["control"]
                if ctrl.startswith("GDPR"):
                    frameworks_affected.add("GDPR")
                elif ctrl.startswith("HIPAA") or ctrl.startswith("164."):
                    frameworks_affected.add("HIPAA")
                elif ctrl.startswith("CC") or ctrl.startswith("SOC2"):
                    frameworks_affected.add("SOC2")

        severity_counts = {}
        for v in all_violations:
            sev = v.get("severity", "unknown")
            severity_counts[sev] = severity_counts.get(sev, 0) + 1

        audit_trail = {
            "timestamp": datetime.now(timezone.utc).isoformat(),
            "content_hash": hash(state["content"]) & 0xFFFFFFFF,
            "resource_type": state["resource_type"],
            "frameworks_checked": state["frameworks"],
            "frameworks_affected": list(frameworks_affected),
            "decision": state["decision"],
            "confidence": state["confidence"],
            "violation_count": len(all_violations),
            "severity_breakdown": severity_counts,
            "remediation_action": state["remediation_action"],
            "requires_human_review": state["requires_human_review"],
            "escalation_reason": state["escalation_reason"],
        }

        metrics = {
            "total_checks": len(state["checks_required"]),
            "pii_findings": len(state["pii_findings"]),
            "policy_findings": len(state["policy_findings"]),
            "llm_findings": len(state["llm_findings"]),
            "false_positive_rate": 0.0,
        }

        return {"audit_trail": audit_trail, "metrics": metrics}


# --- LangGraph Workflow ---

def build_compliance_workflow(opa_url: str, ollama_url: str) -> StateGraph:
    pii_agent = PiiDetectionAgent(opa_url, ollama_url)
    policy_agent = PolicyViolationAgent(opa_url)
    remediation_agent = RemediationAgent(ollama_url)
    reporting_agent = ReportingAgent()

    async def intake(state: ComplianceState) -> dict:
        return {
            "pii_findings": [],
            "policy_findings": [],
            "security_findings": [],
            "llm_findings": [],
            "confidence": 0.0,
            "decision": "pending",
            "requires_human_review": False,
            "escalation_reason": "",
            "remediation_action": "none",
            "remediated_content": "",
            "audit_trail": {},
            "metrics": {},
        }

    async def plan(state: ComplianceState) -> dict:
        checks = ["pii"]
        if state["source"] in ("user_input", "api_request"):
            checks.append("security")
        if state["frameworks"]:
            checks.append("compliance")
        checks.append("safety")
        if state["resource_type"] in ("text", "email", "chat"):
            checks.append("brand")
        return {"checks_required": checks}

    async def execute_pii(state: ComplianceState) -> dict:
        return await pii_agent.detect(state)

    async def execute_policy(state: ComplianceState) -> dict:
        return await policy_agent.check(state)

    async def evaluate(state: ComplianceState) -> dict:
        all_violations = (
            state["pii_findings"]
            + state["policy_findings"]
            + state["llm_findings"]
        )

        if not all_violations:
            return {"decision": "allow", "confidence": 1.0, "requires_human_review": False, "escalation_reason": ""}

        severities = [v.get("severity", "medium") for v in all_violations]
        has_critical = "critical" in severities

        confidence_scores = [v.get("confidence", 0.8) for v in all_violations if "confidence" in v]
        avg_confidence = sum(confidence_scores) / len(confidence_scores) if confidence_scores else 0.8

        if has_critical:
            decision = "block"
            confidence = max(0.9, avg_confidence)
        elif "high" in severities:
            decision = "redact" if any(v.get("remediation") == "redact" for v in all_violations) else "block"
            confidence = avg_confidence
        else:
            decision = "escalate"
            confidence = avg_confidence

        needs_review = confidence < 0.7 or (not has_critical and len(all_violations) > 3)
        reason = ""
        if needs_review:
            if confidence < 0.7:
                reason = f"Low confidence ({confidence:.2f}) — requires human verification"
            else:
                reason = f"Multiple findings ({len(all_violations)}) across categories"

        return {
            "decision": decision,
            "confidence": round(confidence, 3),
            "requires_human_review": needs_review,
            "escalation_reason": reason,
        }

    async def remediate(state: ComplianceState) -> dict:
        return await remediation_agent.plan_remediation(state)

    def report(state: ComplianceState) -> dict:
        return reporting_agent.generate_report(state)

    def should_escalate(state: ComplianceState) -> Literal["remediate", "escalate"]:
        if state["requires_human_review"]:
            return "escalate"
        return "remediate"

    # Build the graph
    graph = StateGraph(ComplianceState)

    graph.add_node("intake", intake)
    graph.add_node("plan", plan)
    graph.add_node("execute_pii", execute_pii)
    graph.add_node("execute_policy", execute_policy)
    graph.add_node("evaluate", evaluate)
    graph.add_node("remediate", remediate)
    graph.add_node("escalate", lambda s: {
        "remediation_action": "escalate",
        "remediated_content": s["content"],
    })
    graph.add_node("report", report)

    graph.set_entry_point("intake")
    graph.add_edge("intake", "plan")
    graph.add_edge("plan", "execute_pii")
    graph.add_edge("execute_pii", "execute_policy")
    graph.add_edge("execute_policy", "evaluate")
    graph.add_conditional_edges("evaluate", should_escalate, {
        "remediate": "remediate",
        "escalate": "escalate",
    })
    graph.add_edge("remediate", "report")
    graph.add_edge("escalate", "report")
    graph.add_edge("report", END)

    return graph.compile()
