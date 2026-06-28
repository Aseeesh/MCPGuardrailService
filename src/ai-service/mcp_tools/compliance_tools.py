"""
Custom MCP tools for compliance automation.

Tools:
  - check_compliance: Run full compliance check against frameworks
  - detect_pii: Scan content for PII using deterministic + LLM pipeline
  - generate_report: Generate compliance report for a framework
  - submit_review: Submit findings for human review
  - get_review_status: Check status of human review
"""

import json
import hashlib
from datetime import datetime, timezone
from typing import Any

import httpx


class ComplianceMCPTools:
    def __init__(self, opa_url: str, ollama_url: str, redis_url: str | None = None):
        self.opa_url = opa_url
        self.ollama_url = ollama_url
        self.redis_url = redis_url
        self._review_queue: list[dict] = []

    async def check_compliance(
        self,
        content: str,
        frameworks: list[str],
        resource_type: str = "text",
        source: str = "user_input",
    ) -> dict:
        """MCP Tool: Run full compliance check against specified frameworks."""
        results = {"framework_results": {}, "overall_decision": "allow", "violations": []}

        policy_map = {
            "GDPR": ["pii/gdpr"],
            "HIPAA": ["pii/hipaa"],
            "SOC2": ["pii/soc2", "security/injection"],
            "INTERNAL": ["safety/content", "brand/tone"],
        }

        async with httpx.AsyncClient() as client:
            for framework in frameworks:
                packages = policy_map.get(framework, [])
                fw_violations = []

                for pkg in packages:
                    try:
                        resp = await client.post(
                            f"{self.opa_url}/v1/data/{pkg}/violations",
                            json={"input": {
                                "content": content,
                                "context": {
                                    "purpose": "", "retention_days": 0,
                                    "cross_border": False, "data_category": "",
                                    "source": source,
                                    "hipaa_authorized": False,
                                    "encryption": "none",
                                    "minimum_necessary": False,
                                    "authorized_security_research": False,
                                    "comparison_approved": False,
                                    "content_domain": "",
                                },
                            }},
                        )
                        if resp.status_code == 200:
                            violations = resp.json().get("result", [])
                            fw_violations.extend(violations)
                    except httpx.RequestError:
                        continue

                status = "compliant" if not fw_violations else "non_compliant"
                results["framework_results"][framework] = {
                    "status": status,
                    "violation_count": len(fw_violations),
                    "violations": fw_violations,
                }
                results["violations"].extend(fw_violations)

        if results["violations"]:
            severities = [v.get("severity", "medium") for v in results["violations"]]
            if "critical" in severities:
                results["overall_decision"] = "block"
            elif "high" in severities:
                results["overall_decision"] = "redact"
            else:
                results["overall_decision"] = "escalate"

        results["checked_at"] = datetime.now(timezone.utc).isoformat()
        return results

    async def detect_pii(self, content: str, frameworks: list[str] | None = None) -> dict:
        """MCP Tool: Scan content for PII using OPA + LLM."""
        frameworks = frameworks or ["GDPR", "HIPAA"]
        pii_findings = []

        # OPA deterministic check
        async with httpx.AsyncClient() as client:
            for fw in frameworks:
                pkg = f"pii/{fw.lower()}"
                try:
                    resp = await client.post(
                        f"{self.opa_url}/v1/data/{pkg}/violations",
                        json={"input": {
                            "content": content,
                            "context": {
                                "purpose": "", "retention_days": 0,
                                "cross_border": False, "data_category": "",
                                "hipaa_authorized": False,
                                "encryption": "none",
                                "minimum_necessary": False,
                            },
                        }},
                    )
                    if resp.status_code == 200:
                        pii_findings.extend(resp.json().get("result", []))
                except httpx.RequestError:
                    continue

        # LLM semantic PII check
        llm_findings = await self._llm_pii_scan(content)

        all_findings = pii_findings + llm_findings
        return {
            "pii_detected": len(all_findings) > 0,
            "finding_count": len(all_findings),
            "deterministic_findings": pii_findings,
            "semantic_findings": llm_findings,
            "recommendation": "redact" if all_findings else "allow",
        }

    async def generate_report(self, framework: str) -> dict:
        """MCP Tool: Generate compliance coverage report."""
        control_catalogs = {
            "GDPR": {
                "ART5": "Principles of Processing",
                "ART6": "Lawful Basis",
                "ART9": "Special Categories",
                "ART17": "Right to Erasure",
                "ART25": "Data Protection by Design",
                "ART33": "Breach Notification",
                "ART35": "DPIA",
                "ART46": "Cross-Border Transfer",
            },
            "HIPAA": {
                "164.308": "Administrative Safeguards",
                "164.312": "Technical Safeguards",
                "164.502": "Uses and Disclosures",
                "164.514": "De-identification",
                "164.524": "Access Rights",
            },
            "SOC2": {
                "CC6.1": "Logical Access",
                "CC6.5": "System Boundaries",
                "CC6.7": "Data Transmission",
                "CC7.2": "Monitoring",
                "CC8.1": "Change Management",
            },
        }

        catalog = control_catalogs.get(framework, {})
        covered_controls = await self._get_covered_controls(framework)

        controls = []
        for ctrl_id, ctrl_name in catalog.items():
            controls.append({
                "id": ctrl_id,
                "name": ctrl_name,
                "status": "covered" if ctrl_id in covered_controls else "gap",
                "policies": covered_controls.get(ctrl_id, []),
            })

        covered = sum(1 for c in controls if c["status"] == "covered")
        return {
            "framework": framework,
            "generated_at": datetime.now(timezone.utc).isoformat(),
            "total_controls": len(controls),
            "covered": covered,
            "gaps": len(controls) - covered,
            "coverage_pct": round(covered / len(controls) * 100, 1) if controls else 0,
            "controls": controls,
        }

    async def submit_review(
        self,
        content: str,
        decision: str,
        confidence: float,
        violations: list[dict],
        reason: str,
    ) -> dict:
        """MCP Tool: Submit findings for human-in-the-loop review."""
        review_id = hashlib.sha256(
            f"{content}{datetime.now(timezone.utc).isoformat()}".encode()
        ).hexdigest()[:12]

        review = {
            "review_id": review_id,
            "status": "pending",
            "submitted_at": datetime.now(timezone.utc).isoformat(),
            "content_preview": content[:200] + ("..." if len(content) > 200 else ""),
            "auto_decision": decision,
            "confidence": confidence,
            "violation_count": len(violations),
            "violations": violations[:10],
            "reason": reason,
            "reviewer": None,
            "review_decision": None,
            "review_notes": None,
        }

        self._review_queue.append(review)
        return {"review_id": review_id, "status": "submitted", "queue_position": len(self._review_queue)}

    async def get_review_status(self, review_id: str) -> dict:
        """MCP Tool: Check status of a human review."""
        for review in self._review_queue:
            if review["review_id"] == review_id:
                return review
        return {"error": "Review not found", "review_id": review_id}

    async def approve_review(self, review_id: str, reviewer: str, decision: str, notes: str = "") -> dict:
        """Human review endpoint: approve/reject a review item."""
        for review in self._review_queue:
            if review["review_id"] == review_id:
                review["status"] = "completed"
                review["reviewer"] = reviewer
                review["review_decision"] = decision
                review["review_notes"] = notes
                review["reviewed_at"] = datetime.now(timezone.utc).isoformat()
                return review
        return {"error": "Review not found"}

    async def get_pending_reviews(self) -> list[dict]:
        """Get all pending human reviews."""
        return [r for r in self._review_queue if r["status"] == "pending"]

    async def _llm_pii_scan(self, content: str) -> list[dict]:
        prompt = f"""Scan this text for PII that regex patterns might miss.
Look for: names in context, indirect identifiers, health info in natural language,
financial details described narratively.

Text: {content}

Respond in JSON:
{{"findings": [{{"type": "pii_type", "evidence": "exact text", "confidence": 0.0-1.0}}]}}"""

        async with httpx.AsyncClient() as client:
            try:
                resp = await client.post(
                    f"{self.ollama_url}/api/generate",
                    json={"model": "mistral", "prompt": prompt, "stream": False},
                    timeout=30.0,
                )
                raw = resp.json().get("response", "{}")
                start = raw.find("{")
                end = raw.rfind("}") + 1
                if start >= 0 and end > start:
                    parsed = json.loads(raw[start:end])
                    return parsed.get("findings", [])
            except (httpx.RequestError, json.JSONDecodeError):
                pass
        return []

    async def _get_covered_controls(self, framework: str) -> dict[str, list[str]]:
        coverage = {
            "GDPR": {"ART5": ["gdpr_pii.rego"], "ART6": ["gdpr_pii.rego"], "ART9": ["gdpr_pii.rego"], "ART46": ["gdpr_pii.rego"]},
            "HIPAA": {"164.312": ["hipaa_phi.rego"], "164.502": ["hipaa_phi.rego"], "164.514": ["hipaa_phi.rego"]},
            "SOC2": {"CC6.1": ["soc2_data.rego", "injection.rego"], "CC7.2": ["soc2_data.rego"]},
        }
        return coverage.get(framework, {})


MCP_COMPLIANCE_TOOLS = [
    {
        "name": "check_compliance",
        "description": "Run full compliance check against GDPR/HIPAA/SOC2 frameworks",
        "inputSchema": {
            "type": "object",
            "properties": {
                "content": {"type": "string", "description": "Content to check"},
                "frameworks": {"type": "array", "items": {"type": "string"}, "description": "Frameworks to check against"},
                "resource_type": {"type": "string", "default": "text"},
                "source": {"type": "string", "default": "user_input"},
            },
            "required": ["content", "frameworks"],
        },
    },
    {
        "name": "detect_pii",
        "description": "Scan content for PII using deterministic patterns and LLM semantic analysis",
        "inputSchema": {
            "type": "object",
            "properties": {
                "content": {"type": "string"},
                "frameworks": {"type": "array", "items": {"type": "string"}},
            },
            "required": ["content"],
        },
    },
    {
        "name": "generate_report",
        "description": "Generate compliance coverage report for a framework",
        "inputSchema": {
            "type": "object",
            "properties": {
                "framework": {"type": "string", "enum": ["GDPR", "HIPAA", "SOC2"]},
            },
            "required": ["framework"],
        },
    },
    {
        "name": "submit_review",
        "description": "Submit findings for human-in-the-loop review",
        "inputSchema": {
            "type": "object",
            "properties": {
                "content": {"type": "string"},
                "decision": {"type": "string"},
                "confidence": {"type": "number"},
                "violations": {"type": "array"},
                "reason": {"type": "string"},
            },
            "required": ["content", "decision", "confidence", "violations", "reason"],
        },
    },
    {
        "name": "get_review_status",
        "description": "Check status of a human review",
        "inputSchema": {
            "type": "object",
            "properties": {
                "review_id": {"type": "string"},
            },
            "required": ["review_id"],
        },
    },
]
