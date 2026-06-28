"""
Audit report generator — produces compliance audit reports from audit trail data.
Supports GDPR, HIPAA, SOC2 framework-specific reports.
"""

import hashlib
import json
from datetime import datetime, timezone
from typing import Any


class AuditReportGenerator:
    def __init__(self, opa_url: str):
        self.opa_url = opa_url

    def generate_compliance_report(
        self,
        framework: str,
        audit_entries: list[dict],
        period_start: str,
        period_end: str,
        tenant_id: str = "default",
    ) -> dict[str, Any]:
        total = len(audit_entries)
        decisions = {}
        severities = {}
        controls_hit = set()
        human_reviews = 0
        avg_confidence = 0.0
        avg_pipeline_ms = 0.0
        violations_by_rule: dict[str, int] = {}

        for entry in audit_entries:
            dec = entry.get("decision", "unknown")
            decisions[dec] = decisions.get(dec, 0) + 1

            if entry.get("human_reviewed"):
                human_reviews += 1

            avg_confidence += entry.get("confidence", 0)
            avg_pipeline_ms += entry.get("pipeline_ms", 0)

            for v in entry.get("violations", []):
                sev = v.get("severity", "unknown")
                severities[sev] = severities.get(sev, 0) + 1
                if v.get("control_id"):
                    controls_hit.add(v["control_id"])
                rule = v.get("rule_id", "unknown")
                violations_by_rule[rule] = violations_by_rule.get(rule, 0) + 1

        if total > 0:
            avg_confidence /= total
            avg_pipeline_ms /= total

        control_catalog = self._get_control_catalog(framework)
        controls_covered = controls_hit & set(control_catalog.keys())
        controls_gap = set(control_catalog.keys()) - controls_hit

        report = {
            "report_id": hashlib.sha256(
                f"{tenant_id}{framework}{period_start}".encode()
            ).hexdigest()[:16],
            "framework": framework,
            "tenant_id": tenant_id,
            "generated_at": datetime.now(timezone.utc).isoformat(),
            "period": {"start": period_start, "end": period_end},
            "summary": {
                "total_checks": total,
                "decisions": decisions,
                "violation_count": sum(severities.values()),
                "severity_breakdown": severities,
                "human_reviews": human_reviews,
                "human_review_pct": round(human_reviews / total * 100, 1) if total else 0,
                "avg_confidence": round(avg_confidence, 4),
                "avg_pipeline_ms": round(avg_pipeline_ms, 1),
            },
            "compliance": {
                "total_controls": len(control_catalog),
                "covered": len(controls_covered),
                "gaps": len(controls_gap),
                "coverage_pct": round(
                    len(controls_covered) / len(control_catalog) * 100, 1
                ) if control_catalog else 0,
                "controls": [
                    {
                        "id": cid,
                        "name": cname,
                        "status": "covered" if cid in controls_covered else "gap",
                    }
                    for cid, cname in control_catalog.items()
                ],
            },
            "top_violations": sorted(
                violations_by_rule.items(), key=lambda x: x[1], reverse=True
            )[:10],
            "recommendations": self._generate_recommendations(
                framework, controls_gap, decisions, severities
            ),
            "integrity": {
                "report_hash": "",  # filled below
                "records_verified": total,
            },
        }

        report["integrity"]["report_hash"] = hashlib.sha256(
            json.dumps(report["summary"], sort_keys=True).encode()
        ).hexdigest()

        return report

    def generate_executive_summary(
        self, reports: list[dict]
    ) -> dict[str, Any]:
        total_checks = sum(r["summary"]["total_checks"] for r in reports)
        total_violations = sum(r["summary"]["violation_count"] for r in reports)

        return {
            "generated_at": datetime.now(timezone.utc).isoformat(),
            "frameworks_assessed": [r["framework"] for r in reports],
            "overall": {
                "total_checks": total_checks,
                "total_violations": total_violations,
                "violation_rate": round(total_violations / total_checks * 100, 2) if total_checks else 0,
                "avg_confidence": round(
                    sum(r["summary"]["avg_confidence"] for r in reports) / len(reports), 4
                ) if reports else 0,
            },
            "per_framework": [
                {
                    "framework": r["framework"],
                    "coverage_pct": r["compliance"]["coverage_pct"],
                    "checks": r["summary"]["total_checks"],
                    "violations": r["summary"]["violation_count"],
                    "gaps": r["compliance"]["gaps"],
                }
                for r in reports
            ],
            "critical_gaps": [
                {"framework": r["framework"], "control": c["id"], "name": c["name"]}
                for r in reports
                for c in r["compliance"]["controls"]
                if c["status"] == "gap"
            ],
        }

    def _generate_recommendations(
        self,
        framework: str,
        gaps: set,
        decisions: dict,
        severities: dict,
    ) -> list[str]:
        recs = []

        if gaps:
            recs.append(
                f"Address {len(gaps)} control gap(s): {', '.join(sorted(gaps)[:5])}"
            )

        block_rate = decisions.get("block", 0)
        total = sum(decisions.values())
        if total > 0 and block_rate / total > 0.2:
            recs.append(
                "High block rate ({:.0%}) — review input validation and user guidance".format(
                    block_rate / total
                )
            )

        critical = severities.get("critical", 0)
        if critical > 0:
            recs.append(
                f"{critical} critical violations detected — immediate remediation required"
            )

        escalate = decisions.get("escalate", 0)
        if total > 0 and escalate / total > 0.15:
            recs.append(
                "Escalation rate above 15% — consider tuning detection thresholds to reduce human review load"
            )

        if not recs:
            recs.append("No critical issues found. Continue monitoring policy effectiveness.")

        return recs

    def _get_control_catalog(self, framework: str) -> dict[str, str]:
        catalogs = {
            "GDPR": {
                "ART5": "Principles of Processing",
                "ART6": "Lawful Basis",
                "ART9": "Special Categories",
                "ART12": "Transparent Information",
                "ART17": "Right to Erasure",
                "ART25": "Data Protection by Design",
                "ART30": "Records of Processing",
                "ART32": "Security of Processing",
                "ART33": "Breach Notification",
                "ART35": "DPIA",
                "ART46": "Cross-Border Transfer",
            },
            "HIPAA": {
                "164.308": "Administrative Safeguards",
                "164.310": "Physical Safeguards",
                "164.312": "Technical Safeguards",
                "164.502": "Uses and Disclosures",
                "164.514": "De-identification",
                "164.524": "Access Rights",
            },
            "SOC2": {
                "CC6.1": "Logical Access",
                "CC6.3": "Role-Based Access",
                "CC6.5": "System Boundaries",
                "CC6.7": "Data Transmission",
                "CC7.2": "Monitoring",
                "CC8.1": "Change Management",
            },
        }
        return catalogs.get(framework, {})
