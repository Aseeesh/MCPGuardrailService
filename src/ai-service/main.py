from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from agents.compliance_agent import ComplianceAgent
from agents.langgraph_agents import build_compliance_workflow
from mcp_tools.guardrail_tools import GuardrailMCPTools
from mcp_tools.compliance_tools import ComplianceMCPTools
from pipeline.llm_judge import LlmJudge
from remediation.rewrite_engine import RewriteEngine
from remediation.review_queue import ReviewQueue
import httpx
import os
import json

app = FastAPI(title="Guardrail AI Service", version="2.0.0")

ollama_url = os.getenv("OLLAMA_BASE_URL", "http://localhost:11434")
opa_url = os.getenv("OPA_URL", "http://localhost:8181")
redis_url = os.getenv("REDIS_URL", "redis://localhost:6379")

agent = ComplianceAgent(ollama_url=ollama_url, opa_url=opa_url)
mcp_tools = GuardrailMCPTools()
compliance_tools = ComplianceMCPTools(opa_url=opa_url, ollama_url=ollama_url, redis_url=redis_url)
llm_judge = LlmJudge(ollama_url=ollama_url)
compliance_workflow = build_compliance_workflow(opa_url=opa_url, ollama_url=ollama_url)
rewrite_engine = RewriteEngine(ollama_url=ollama_url)
review_queue = ReviewQueue()


class ValidateRequest(BaseModel):
    content: str
    resource_type: str
    frameworks: list[str]


class RemediateRequest(BaseModel):
    content: str
    resource_type: str
    action: str  # redact, rewrite, block, escalate


class ScanRequest(BaseModel):
    frameworks: list[str]
    resource_group: str | None = None
    resource_types: list[str] = []


@app.post("/api/validate")
async def validate_content(request: ValidateRequest):
    opa_results = await _check_opa_policies(request.content, request.frameworks)

    llm_judgment = await agent.judge_content(
        content=request.content,
        resource_type=request.resource_type,
        frameworks=request.frameworks,
    )

    violations = opa_results.get("violations", []) + llm_judgment.get("violations", [])
    decision = "block" if violations else "allow"

    return {
        "decision": decision,
        "reasoning": llm_judgment.get("reasoning", ""),
        "violations": violations,
    }


@app.post("/api/remediate")
async def remediate_content(request: RemediateRequest):
    result = await agent.remediate(
        content=request.content,
        resource_type=request.resource_type,
        action=request.action,
    )
    return result


@app.post("/api/compliance/scan")
async def compliance_scan(request: ScanRequest):
    findings = []
    for framework in request.frameworks:
        framework_findings = await agent.scan_compliance(
            framework=framework,
            resource_group=request.resource_group,
            resource_types=request.resource_types,
        )
        findings.extend(framework_findings)
    return findings


@app.post("/api/mcp/validate")
async def mcp_validate():
    return await mcp_tools.validate()


@app.get("/api/mcp/architectures")
async def list_architectures():
    return await mcp_tools.list_architectures()


@app.post("/api/mcp/insights")
async def query_insights(query: dict):
    return await mcp_tools.query_insights(query.get("query", ""))


@app.get("/api/mcp/guidance")
async def guardrail_guidance():
    return await mcp_tools.guardrail_guidance()


async def _check_opa_policies(content: str, frameworks: list[str]) -> dict:
    violations = []
    async with httpx.AsyncClient() as client:
        for framework in frameworks:
            try:
                response = await client.post(
                    f"{opa_url}/v1/data/{framework.lower()}/violations",
                    json={"input": {"content": content}},
                )
                if response.status_code == 200:
                    result = response.json()
                    violations.extend(result.get("result", []))
            except httpx.RequestError:
                continue
    return {"violations": violations}


@app.post("/api/policies/evaluate")
async def evaluate_policies(request: ValidateRequest):
    """Evaluate content against all loaded policy packages (PII, security, safety, brand)."""
    policy_packages = [
        "pii/gdpr", "pii/hipaa", "pii/soc2",
        "security/injection",
        "safety/content",
        "brand/tone",
    ]
    all_violations = []
    decisions = []

    async with httpx.AsyncClient() as client:
        for pkg in policy_packages:
            try:
                resp = await client.post(
                    f"{opa_url}/v1/data/{pkg}/violations",
                    json={"input": {
                        "content": request.content,
                        "context": {
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
                        },
                    }},
                )
                if resp.status_code == 200:
                    violations = resp.json().get("result", [])
                    all_violations.extend(violations)

                resp_d = await client.post(
                    f"{opa_url}/v1/data/{pkg}/decision",
                    json={"input": {"content": request.content, "context": {}}},
                )
                if resp_d.status_code == 200:
                    decisions.append({"policy": pkg, "decision": resp_d.json().get("result", "unknown")})
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
    }


@app.post("/api/policies/reload")
async def reload_policies():
    """Hot-reload all Rego policies into OPA."""
    from pathlib import Path
    import hashlib

    policy_dir = Path("/app/../policy-engine/policies")
    if not policy_dir.exists():
        policy_dir = Path(__file__).parent.parent / "policy-engine" / "policies"

    loaded = []
    errors = []
    async with httpx.AsyncClient() as client:
        for rego_file in policy_dir.rglob("*.rego"):
            if "_test" in rego_file.name:
                continue
            try:
                content = rego_file.read_text()
                rel = str(rego_file.relative_to(policy_dir))
                policy_id = rel.replace("/", ".").replace(".rego", "")
                resp = await client.put(
                    f"{opa_url}/v1/policies/{policy_id}",
                    content=content,
                    headers={"Content-Type": "text/plain"},
                )
                if resp.status_code == 200:
                    loaded.append(policy_id)
                else:
                    errors.append({"policy": policy_id, "error": resp.text})
            except Exception as e:
                errors.append({"policy": str(rego_file), "error": str(e)})

    return {"loaded": loaded, "errors": errors}


@app.get("/api/policies/status")
async def policy_status():
    """List all policies loaded in OPA."""
    async with httpx.AsyncClient() as client:
        try:
            resp = await client.get(f"{opa_url}/v1/policies")
            if resp.status_code == 200:
                policies = resp.json().get("result", [])
                return {
                    "total": len(policies),
                    "policies": [{"id": p.get("id"), "path": p.get("path", "")} for p in policies],
                }
        except httpx.RequestError:
            pass
    return {"total": 0, "policies": [], "error": "OPA unavailable"}


# --- LLM-as-Judge Endpoint ---

class LlmJudgeRequest(BaseModel):
    content: str
    resource_type: str = "text"
    frameworks: list[str] = []
    existing_violations: list[dict] | None = None


@app.post("/api/llm/judge")
async def llm_judge_endpoint(request: LlmJudgeRequest):
    """LLM-as-Judge: semantic analysis across all policy types."""
    result = await llm_judge.judge_all_types(
        content=request.content,
        frameworks=request.frameworks,
        resource_type=request.resource_type,
    )
    return result


# --- LangGraph Compliance Workflow ---

class WorkflowRequest(BaseModel):
    content: str
    resource_type: str = "text"
    frameworks: list[str] = ["GDPR", "HIPAA", "SOC2"]
    source: str = "user_input"


@app.post("/api/workflow/run")
async def run_compliance_workflow(request: WorkflowRequest):
    """Run full LangGraph compliance workflow: intake → plan → execute → evaluate → remediate → report."""
    initial_state = {
        "content": request.content,
        "resource_type": request.resource_type,
        "frameworks": request.frameworks,
        "source": request.source,
        "checks_required": [],
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
    result = await compliance_workflow.ainvoke(initial_state)
    return {
        "decision": result["decision"],
        "confidence": result["confidence"],
        "remediation_action": result["remediation_action"],
        "requires_human_review": result["requires_human_review"],
        "escalation_reason": result["escalation_reason"],
        "audit_trail": result["audit_trail"],
        "metrics": result["metrics"],
        "violation_count": len(result["pii_findings"]) + len(result["policy_findings"]) + len(result["llm_findings"]),
    }


# --- MCP Compliance Tools ---

@app.post("/api/mcp/check_compliance")
async def mcp_check_compliance(request: WorkflowRequest):
    return await compliance_tools.check_compliance(
        content=request.content,
        frameworks=request.frameworks,
        resource_type=request.resource_type,
        source=request.source,
    )


@app.post("/api/mcp/detect_pii")
async def mcp_detect_pii(data: dict):
    return await compliance_tools.detect_pii(
        content=data.get("content", ""),
        frameworks=data.get("frameworks"),
    )


@app.get("/api/mcp/report/{framework}")
async def mcp_generate_report(framework: str):
    return await compliance_tools.generate_report(framework)


# --- Rewrite Engine ---

class RewriteRequest(BaseModel):
    content: str
    rewrite_type: str | None = None
    violations: list[dict] = []
    frameworks: list[str] = []


@app.post("/api/rewrite")
async def rewrite_content(request: RewriteRequest):
    """Rewrite content using LLM with tone adjustment, uncertainty, or compliance fixes."""
    if request.rewrite_type:
        return await rewrite_engine.rewrite(
            content=request.content,
            rewrite_type=request.rewrite_type,
            violations=request.violations,
            frameworks=request.frameworks,
        )
    return await rewrite_engine.auto_select_and_rewrite(
        content=request.content,
        violations=request.violations,
        frameworks=request.frameworks,
    )


# --- Human-in-the-Loop Review Queue ---

class ReviewSubmission(BaseModel):
    content: str
    decision: str
    confidence: float
    violations: list[dict]
    reason: str
    ai_output: str | None = None
    frameworks: list[str] = []


@app.post("/api/review/submit")
async def submit_review(request: ReviewSubmission):
    item = review_queue.submit(
        content=request.content,
        decision=request.decision,
        confidence=request.confidence,
        violations=request.violations,
        reason=request.reason,
        ai_output=request.ai_output,
        frameworks=request.frameworks,
    )
    return item.model_dump()


@app.get("/api/review/pending")
async def get_pending_reviews(limit: int = 50):
    items = review_queue.get_pending(limit)
    return [item.model_dump() for item in items]


@app.get("/api/review/stats")
async def get_review_stats():
    return review_queue.get_stats()


@app.get("/api/review/feedback/stats")
async def get_feedback_stats():
    return review_queue.get_feedback_stats()


@app.get("/api/review/{review_id}")
async def get_review_item(review_id: str):
    item = review_queue.get_item(review_id)
    if not item:
        raise HTTPException(status_code=404, detail="Review not found")
    return item.model_dump()


class ReviewClaim(BaseModel):
    reviewer: str


@app.post("/api/review/{review_id}/claim")
async def claim_review(review_id: str, request: ReviewClaim):
    item = review_queue.claim(review_id, request.reviewer)
    if not item:
        raise HTTPException(status_code=404, detail="Review not found or already completed")
    return item.model_dump()


class ReviewDecision(BaseModel):
    reviewer: str
    decision: str  # approve, reject, modify
    notes: str = ""
    modified_content: str | None = None


@app.post("/api/review/{review_id}/decide")
async def decide_review(review_id: str, request: ReviewDecision):
    if request.decision == "approve":
        item = review_queue.approve(review_id, request.reviewer, request.notes)
    elif request.decision == "reject":
        item = review_queue.reject(review_id, request.reviewer, request.notes)
    elif request.decision == "modify":
        if not request.modified_content:
            raise HTTPException(status_code=400, detail="modified_content required for modify action")
        item = review_queue.modify(review_id, request.reviewer, request.modified_content, request.notes)
    else:
        raise HTTPException(status_code=400, detail="decision must be approve, reject, or modify")

    if not item:
        raise HTTPException(status_code=404, detail="Review not found")
    return item.model_dump()


class FeedbackSubmission(BaseModel):
    reviewer: str
    was_correct: bool
    feedback_notes: str = ""


@app.post("/api/review/{review_id}/feedback")
async def submit_feedback(review_id: str, request: FeedbackSubmission):
    entry = review_queue.submit_feedback(
        review_id=review_id,
        reviewer=request.reviewer,
        was_correct=request.was_correct,
        feedback_notes=request.feedback_notes,
    )
    if not entry:
        raise HTTPException(status_code=404, detail="Review not found")
    return entry.model_dump()


@app.get("/health")
async def health():
    return {"status": "healthy"}
