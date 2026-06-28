"""Golden test runner — validates policies against YAML test cases via live OPA."""

import json
import sys
from pathlib import Path
import httpx
import yaml

OPA_URL = "http://localhost:8181"
TEMPLATE_DIR = Path("src/policy-engine/templates")

POLICY_PKG_MAP = {
    "PII-GDPR-EMAIL-001": "pii/gdpr",
    "PII-HIPAA-PHI-001": "pii/hipaa",
    "SEC-INJ-001": "security/injection",
}


def default_context():
    return {
        "purpose": "",
        "retention_days": 0,
        "cross_border": False,
        "data_category": "",
        "source": "user_input",
        "hipaa_authorized": False,
        "encryption": "none",
        "minimum_necessary": False,
        "authorized_security_research": False,
        "comparison_approved": False,
        "content_domain": "",
    }


def run():
    client = httpx.Client(base_url=OPA_URL)
    results = {"total": 0, "passed": 0, "failed": 0, "errors": [], "details": []}

    for yaml_file in sorted(TEMPLATE_DIR.glob("*.yaml")):
        if yaml_file.name == "policy_template.yaml":
            continue

        doc = yaml.safe_load(yaml_file.read_text())
        policy_name = doc["metadata"]["name"]
        tests = doc.get("spec", {}).get("tests", {})
        pkg = POLICY_PKG_MAP.get(policy_name)
        if not pkg:
            continue

        # Positive tests (should trigger)
        for tc in tests.get("should_trigger", []):
            results["total"] += 1
            try:
                resp = client.post(
                    f"/v1/data/{pkg}/violations",
                    json={"input": {"content": tc["input"], "context": default_context()}},
                )
                violations = resp.json().get("result", [])
                if violations:
                    results["passed"] += 1
                    results["details"].append({
                        "policy": policy_name,
                        "type": "should_trigger",
                        "input": tc["input"][:80],
                        "pass": True,
                    })
                    print(f"  ✓ TRIGGER: {tc['input'][:60]}...")
                else:
                    results["failed"] += 1
                    results["errors"].append(f"{policy_name}: expected trigger on '{tc['input'][:60]}'")
                    results["details"].append({
                        "policy": policy_name,
                        "type": "should_trigger",
                        "input": tc["input"][:80],
                        "pass": False,
                    })
                    print(f"  ✗ TRIGGER: {tc['input'][:60]}...")
            except Exception as e:
                results["failed"] += 1
                results["errors"].append(f"{policy_name}: error - {e}")

        # Negative tests (should pass)
        for tc in tests.get("should_pass", []):
            results["total"] += 1
            try:
                resp = client.post(
                    f"/v1/data/{pkg}/violations",
                    json={"input": {"content": tc["input"], "context": default_context()}},
                )
                violations = resp.json().get("result", [])
                if not violations:
                    results["passed"] += 1
                    results["details"].append({
                        "policy": policy_name,
                        "type": "should_pass",
                        "input": tc["input"][:80],
                        "pass": True,
                    })
                    print(f"  ✓ PASS: {tc['input'][:60]}...")
                else:
                    results["failed"] += 1
                    results["errors"].append(f"{policy_name}: expected pass on '{tc['input'][:60]}'")
                    results["details"].append({
                        "policy": policy_name,
                        "type": "should_pass",
                        "input": tc["input"][:80],
                        "pass": False,
                    })
                    print(f"  ✗ PASS (got {len(violations)} violations): {tc['input'][:60]}...")
            except Exception as e:
                results["failed"] += 1
                results["errors"].append(f"{policy_name}: error - {e}")

    Path("golden-results.json").write_text(json.dumps(results, indent=2))

    print(f"\n{'='*40}")
    print(f"Golden Tests: {results['passed']}/{results['total']} passed")
    if results["errors"]:
        print(f"Failures:")
        for err in results["errors"]:
            print(f"  ✗ {err}")
        sys.exit(1)


if __name__ == "__main__":
    run()
