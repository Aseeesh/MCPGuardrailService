"""
Policy-as-Code Engine — manages policy lifecycle, validation, hot-reload, and compliance reporting.
"""

import json
import hashlib
import subprocess
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import httpx
import yaml


POLICY_DIR = Path(__file__).parent / "policies"
TEMPLATE_DIR = Path(__file__).parent / "templates"
TEST_DIR = Path(__file__).parent / "tests"
MAPPING_DIR = Path(__file__).parent / "mappings"
REPORT_DIR = Path(__file__).parent / "reports"


class PolicyEngine:
    def __init__(self, opa_url: str = "http://localhost:8181"):
        self.opa_url = opa_url
        self._policy_hashes: dict[str, str] = {}
        self._loaded_policies: dict[str, dict] = {}

    async def load_all_policies(self) -> dict[str, Any]:
        results = {"loaded": [], "errors": []}
        for rego_file in POLICY_DIR.rglob("*.rego"):
            if "_test" in rego_file.name:
                continue
            try:
                await self._load_policy(rego_file)
                results["loaded"].append(str(rego_file.relative_to(POLICY_DIR)))
            except Exception as e:
                results["errors"].append({"file": str(rego_file), "error": str(e)})
        return results

    async def _load_policy(self, path: Path) -> None:
        content = path.read_text()
        content_hash = hashlib.sha256(content.encode()).hexdigest()
        rel_path = str(path.relative_to(POLICY_DIR))

        if self._policy_hashes.get(rel_path) == content_hash:
            return

        policy_id = rel_path.replace("/", ".").replace(".rego", "")
        async with httpx.AsyncClient() as client:
            resp = await client.put(
                f"{self.opa_url}/v1/policies/{policy_id}",
                content=content,
                headers={"Content-Type": "text/plain"},
            )
            resp.raise_for_status()

        self._policy_hashes[rel_path] = content_hash
        self._loaded_policies[policy_id] = {
            "path": rel_path,
            "hash": content_hash,
            "loaded_at": datetime.now(timezone.utc).isoformat(),
        }

    async def hot_reload(self) -> dict[str, Any]:
        reloaded = []
        for rego_file in POLICY_DIR.rglob("*.rego"):
            if "_test" in rego_file.name:
                continue
            content = rego_file.read_text()
            content_hash = hashlib.sha256(content.encode()).hexdigest()
            rel_path = str(rego_file.relative_to(POLICY_DIR))

            if self._policy_hashes.get(rel_path) != content_hash:
                await self._load_policy(rego_file)
                reloaded.append(rel_path)

        return {"reloaded": reloaded, "total_loaded": len(self._loaded_policies)}

    async def evaluate(self, content: str, context: dict, policies: list[str] | None = None) -> dict[str, Any]:
        input_data = {"content": content, "context": context}
        all_violations = []
        decisions = []

        policy_packages = policies or list(self._loaded_policies.keys())
        async with httpx.AsyncClient() as client:
            for pkg in policy_packages:
                try:
                    resp = await client.post(
                        f"{self.opa_url}/v1/data/{pkg.replace('.', '/')}/violations",
                        json={"input": input_data},
                    )
                    if resp.status_code == 200:
                        violations = resp.json().get("result", [])
                        all_violations.extend(violations)

                    resp_d = await client.post(
                        f"{self.opa_url}/v1/data/{pkg.replace('.', '/')}/decision",
                        json={"input": input_data},
                    )
                    if resp_d.status_code == 200:
                        decisions.append({
                            "policy": pkg,
                            "decision": resp_d.json().get("result", "unknown"),
                        })
                except httpx.RequestError:
                    continue

        final = "allow"
        for d in decisions:
            if d["decision"] == "block":
                final = "block"
                break
            if d["decision"] in ("redact", "escalate", "rewrite"):
                final = d["decision"]

        return {
            "decision": final,
            "violations": all_violations,
            "policy_decisions": decisions,
            "evaluated_at": datetime.now(timezone.utc).isoformat(),
            "audit_id": hashlib.sha256(
                f"{content}{datetime.now(timezone.utc).isoformat()}".encode()
            ).hexdigest()[:16],
        }

    def run_tests(self) -> dict[str, Any]:
        try:
            result = subprocess.run(
                ["opa", "test", str(POLICY_DIR), str(TEST_DIR), "-v", "--format", "json"],
                capture_output=True, text=True, timeout=60,
            )
            output = json.loads(result.stdout) if result.stdout else []
            passed = sum(1 for t in output if t.get("pass", False))
            failed = sum(1 for t in output if not t.get("pass", False))
            return {
                "total": len(output),
                "passed": passed,
                "failed": failed,
                "results": output,
                "exit_code": result.returncode,
            }
        except (subprocess.TimeoutExpired, FileNotFoundError, json.JSONDecodeError) as e:
            return {"error": str(e), "total": 0, "passed": 0, "failed": 0}

    def load_yaml_policy(self, path: Path) -> dict[str, Any]:
        with open(path) as f:
            policy = yaml.safe_load(f)
        self._validate_yaml_policy(policy)
        return policy

    def _validate_yaml_policy(self, policy: dict) -> None:
        required = ["apiVersion", "kind", "metadata", "spec"]
        for field in required:
            if field not in policy:
                raise ValueError(f"Missing required field: {field}")
        if policy["kind"] != "Policy":
            raise ValueError(f"Invalid kind: {policy['kind']}")
        meta_required = ["name", "version", "description"]
        for field in meta_required:
            if field not in policy["metadata"]:
                raise ValueError(f"Missing metadata field: {field}")
        spec = policy["spec"]
        if "frameworks" not in spec or not spec["frameworks"]:
            raise ValueError("At least one framework mapping is required")
        if "rules" not in spec or not spec["rules"]:
            raise ValueError("At least one rule is required")

    def generate_compliance_report(self, framework: str) -> dict[str, Any]:
        controls_covered = set()
        policy_count = 0

        for yaml_file in TEMPLATE_DIR.rglob("*.yaml"):
            try:
                policy = self.load_yaml_policy(yaml_file)
                for fw in policy["spec"]["frameworks"]:
                    if fw["name"] == framework:
                        controls_covered.update(fw["controls"])
                        policy_count += 1
            except (ValueError, KeyError, yaml.YAMLError):
                continue

        control_catalog = _get_control_catalog(framework)
        total_controls = len(control_catalog)
        covered = len(controls_covered & set(control_catalog.keys()))

        return {
            "framework": framework,
            "generated_at": datetime.now(timezone.utc).isoformat(),
            "summary": {
                "total_controls": total_controls,
                "covered": covered,
                "coverage_pct": round(covered / total_controls * 100, 1) if total_controls else 0,
                "policy_count": policy_count,
            },
            "controls": [
                {
                    "id": ctrl_id,
                    "name": ctrl_name,
                    "status": "covered" if ctrl_id in controls_covered else "gap",
                }
                for ctrl_id, ctrl_name in control_catalog.items()
            ],
        }

    async def rollback_policy(self, policy_id: str) -> dict[str, Any]:
        async with httpx.AsyncClient() as client:
            resp = await client.delete(f"{self.opa_url}/v1/policies/{policy_id}")
            if resp.status_code == 200:
                self._loaded_policies.pop(policy_id, None)
                self._policy_hashes = {
                    k: v for k, v in self._policy_hashes.items()
                    if not k.startswith(policy_id.replace(".", "/"))
                }
                return {"status": "rolled_back", "policy": policy_id}
            return {"status": "error", "code": resp.status_code}

    def get_status(self) -> dict[str, Any]:
        return {
            "loaded_policies": len(self._loaded_policies),
            "policies": self._loaded_policies,
            "policy_dir": str(POLICY_DIR),
        }


def _get_control_catalog(framework: str) -> dict[str, str]:
    catalogs = {
        "GDPR": {
            "ART5": "Principles of Processing",
            "ART6": "Lawful Basis",
            "ART9": "Special Categories",
            "ART12": "Transparent Information",
            "ART13": "Information at Collection",
            "ART15": "Right of Access",
            "ART17": "Right to Erasure",
            "ART20": "Right to Portability",
            "ART25": "Data Protection by Design",
            "ART30": "Records of Processing",
            "ART32": "Security of Processing",
            "ART33": "Breach Notification",
            "ART35": "Data Protection Impact Assessment",
            "ART46": "Cross-Border Transfer Safeguards",
        },
        "HIPAA": {
            "164.308": "Administrative Safeguards",
            "164.310": "Physical Safeguards",
            "164.312": "Technical Safeguards",
            "164.314": "Organizational Requirements",
            "164.316": "Policies and Documentation",
            "164.502": "Uses and Disclosures",
            "164.504": "Business Associates",
            "164.510": "Permitted Uses",
            "164.514": "De-identification Standard",
            "164.522": "Confidential Communications",
            "164.524": "Access Rights",
            "164.526": "Amendment Rights",
            "164.528": "Accounting of Disclosures",
        },
        "SOC2": {
            "CC1.1": "COSO Principle 1",
            "CC2.1": "COSO Principle 13",
            "CC3.1": "COSO Principle 6",
            "CC4.1": "COSO Principle 16",
            "CC5.1": "COSO Principle 10",
            "CC6.1": "Logical Access Security",
            "CC6.3": "Role-Based Access",
            "CC6.5": "System Boundaries",
            "CC6.7": "Data Transmission Protection",
            "CC7.1": "Detection Mechanisms",
            "CC7.2": "Monitoring Activities",
            "CC7.3": "Evaluation of Events",
            "CC8.1": "Change Management",
            "CC9.1": "Risk Mitigation",
        },
    }
    return catalogs.get(framework, {})
