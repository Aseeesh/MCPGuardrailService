"""Policy performance benchmark — measures evaluation latency per policy package."""

import json
import time
import sys
from pathlib import Path
import httpx

OPA_URL = "http://localhost:8181"

BENCHMARK_CASES = [
    {"name": "clean_text", "content": "The weather is nice today and I plan to go hiking."},
    {"name": "email_pii", "content": "Contact john.doe@example.com for details about the project."},
    {"name": "ssn_phi", "content": "Patient SSN: 123-45-6789, admitted for evaluation."},
    {"name": "sql_injection", "content": "'; DROP TABLE users; -- malicious input"},
    {"name": "xss_attack", "content": '<script>alert("xss")</script>'},
    {"name": "credential", "content": "password = SuperSecret123! in the config file"},
    {"name": "mixed_violations", "content": "Email user@test.com, SSN 987-65-4321, password=abc123"},
    {"name": "long_content", "content": "Normal text. " * 500},
]

POLICY_PACKAGES = [
    "pii/gdpr", "pii/hipaa", "pii/soc2",
    "security/injection", "safety/content", "brand/tone",
]

ITERATIONS = 50


def default_context():
    return {
        "purpose": "", "retention_days": 0, "cross_border": False,
        "data_category": "", "source": "user_input",
        "hipaa_authorized": False, "encryption": "none",
        "minimum_necessary": False, "authorized_security_research": False,
        "comparison_approved": False, "content_domain": "",
    }


def run():
    client = httpx.Client(base_url=OPA_URL)
    results = {"benchmarks": [], "summary": {}}

    for case in BENCHMARK_CASES:
        case_results = {"name": case["name"], "content_length": len(case["content"]), "packages": {}}

        for pkg in POLICY_PACKAGES:
            times = []
            for _ in range(ITERATIONS):
                start = time.perf_counter()
                try:
                    client.post(
                        f"/v1/data/{pkg}/violations",
                        json={"input": {"content": case["content"], "context": default_context()}},
                    )
                except httpx.RequestError:
                    continue
                elapsed = (time.perf_counter() - start) * 1000
                times.append(elapsed)

            if times:
                times.sort()
                case_results["packages"][pkg] = {
                    "p50_ms": round(times[len(times) // 2], 2),
                    "p95_ms": round(times[int(len(times) * 0.95)], 2),
                    "p99_ms": round(times[int(len(times) * 0.99)], 2),
                    "avg_ms": round(sum(times) / len(times), 2),
                    "min_ms": round(min(times), 2),
                    "max_ms": round(max(times), 2),
                }

        # Full pipeline (all packages)
        all_times = []
        for _ in range(ITERATIONS):
            start = time.perf_counter()
            for pkg in POLICY_PACKAGES:
                try:
                    client.post(
                        f"/v1/data/{pkg}/violations",
                        json={"input": {"content": case["content"], "context": default_context()}},
                    )
                except httpx.RequestError:
                    pass
            elapsed = (time.perf_counter() - start) * 1000
            all_times.append(elapsed)

        if all_times:
            all_times.sort()
            case_results["full_pipeline"] = {
                "p50_ms": round(all_times[len(all_times) // 2], 2),
                "p95_ms": round(all_times[int(len(all_times) * 0.95)], 2),
                "avg_ms": round(sum(all_times) / len(all_times), 2),
            }

        results["benchmarks"].append(case_results)
        p50 = case_results.get("full_pipeline", {}).get("p50_ms", 0)
        status = "✓" if p50 < 50 else "⚠"
        print(f"  {status} {case['name']}: p50={p50}ms (pipeline)")

    # Summary
    all_p50 = [b["full_pipeline"]["p50_ms"] for b in results["benchmarks"] if "full_pipeline" in b]
    results["summary"] = {
        "total_cases": len(BENCHMARK_CASES),
        "iterations_per_case": ITERATIONS,
        "avg_pipeline_p50_ms": round(sum(all_p50) / len(all_p50), 2) if all_p50 else 0,
        "max_pipeline_p50_ms": round(max(all_p50), 2) if all_p50 else 0,
        "meets_sla": all(p < 50 for p in all_p50) if all_p50 else False,
    }

    Path("benchmark-results.json").write_text(json.dumps(results, indent=2))

    print(f"\n{'='*40}")
    print(f"Avg pipeline p50: {results['summary']['avg_pipeline_p50_ms']}ms")
    print(f"Meets <50ms SLA: {results['summary']['meets_sla']}")

    if not results["summary"]["meets_sla"]:
        print("::warning::Some benchmarks exceeded 50ms SLA target")


if __name__ == "__main__":
    run()
