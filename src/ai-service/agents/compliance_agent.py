import httpx
import json
from langgraph.graph import StateGraph, END
from typing import TypedDict


class AgentState(TypedDict):
    content: str
    resource_type: str
    frameworks: list[str]
    opa_results: dict
    llm_judgment: dict
    final_decision: str
    violations: list[str]


class ComplianceAgent:
    def __init__(self, ollama_url: str, opa_url: str):
        self.ollama_url = ollama_url
        self.opa_url = opa_url
        self.model = "mistral"
        self._build_graph()

    def _build_graph(self):
        graph = StateGraph(AgentState)
        graph.add_node("check_policies", self._check_policies_node)
        graph.add_node("llm_judge", self._llm_judge_node)
        graph.add_node("decide", self._decide_node)

        graph.set_entry_point("check_policies")
        graph.add_edge("check_policies", "llm_judge")
        graph.add_edge("llm_judge", "decide")
        graph.add_edge("decide", END)

        self.workflow = graph.compile()

    async def _check_policies_node(self, state: AgentState) -> dict:
        violations = []
        async with httpx.AsyncClient() as client:
            for framework in state["frameworks"]:
                try:
                    resp = await client.post(
                        f"{self.opa_url}/v1/data/{framework.lower()}/violations",
                        json={"input": {"content": state["content"]}},
                    )
                    if resp.status_code == 200:
                        violations.extend(resp.json().get("result", []))
                except httpx.RequestError:
                    continue
        return {"opa_results": {"violations": violations}}

    async def _llm_judge_node(self, state: AgentState) -> dict:
        prompt = f"""You are a compliance judge. Analyze the following content for violations
of {', '.join(state['frameworks'])} frameworks.

Content: {state['content']}
Resource Type: {state['resource_type']}

Respond in JSON format:
{{"violations": ["list of violations"], "reasoning": "explanation", "severity": "low|medium|high|critical"}}"""

        async with httpx.AsyncClient() as client:
            try:
                resp = await client.post(
                    f"{self.ollama_url}/api/generate",
                    json={"model": self.model, "prompt": prompt, "stream": False},
                    timeout=60.0,
                )
                result = resp.json()
                parsed = json.loads(result.get("response", "{}"))
                return {"llm_judgment": parsed}
            except (httpx.RequestError, json.JSONDecodeError):
                return {"llm_judgment": {"violations": [], "reasoning": "LLM unavailable"}}

    async def _decide_node(self, state: AgentState) -> dict:
        all_violations = (
            state.get("opa_results", {}).get("violations", [])
            + state.get("llm_judgment", {}).get("violations", [])
        )
        decision = "block" if all_violations else "allow"
        return {"final_decision": decision, "violations": all_violations}

    async def judge_content(
        self, content: str, resource_type: str, frameworks: list[str]
    ) -> dict:
        state: AgentState = {
            "content": content,
            "resource_type": resource_type,
            "frameworks": frameworks,
            "opa_results": {},
            "llm_judgment": {},
            "final_decision": "",
            "violations": [],
        }
        result = await self.workflow.ainvoke(state)
        return {
            "decision": result["final_decision"],
            "reasoning": result.get("llm_judgment", {}).get("reasoning", ""),
            "violations": result["violations"],
        }

    async def remediate(self, content: str, resource_type: str, action: str) -> dict:
        actions = {
            "redact": "Redact all PII and sensitive information, replacing with [REDACTED].",
            "rewrite": "Rewrite to be compliant while preserving intent.",
            "block": "Explain why this content is blocked.",
            "escalate": "Summarize compliance concerns for human review.",
        }

        prompt = f"""You are a compliance remediation agent.
Action: {actions.get(action, actions['block'])}
Content: {content}
Resource Type: {resource_type}

Respond in JSON: {{"action": "{action}", "modified_content": "...", "explanation": "..."}}"""

        async with httpx.AsyncClient() as client:
            try:
                resp = await client.post(
                    f"{self.ollama_url}/api/generate",
                    json={"model": self.model, "prompt": prompt, "stream": False},
                    timeout=60.0,
                )
                result = resp.json()
                return json.loads(result.get("response", "{}"))
            except (httpx.RequestError, json.JSONDecodeError):
                return {
                    "action": action,
                    "modified_content": "[Content blocked pending review]",
                    "explanation": "Automated remediation unavailable",
                }

    async def scan_compliance(
        self, framework: str, resource_group: str | None, resource_types: list[str]
    ) -> list[dict]:
        prompt = f"""You are a cloud compliance scanner. Generate realistic compliance findings for:
Framework: {framework}
Resource Group: {resource_group or 'default'}
Resource Types: {', '.join(resource_types) or 'all'}

Return a JSON array of findings, each with: framework, control_id, status (pass/fail/warning), description, remediation, severity."""

        async with httpx.AsyncClient() as client:
            try:
                resp = await client.post(
                    f"{self.ollama_url}/api/generate",
                    json={"model": self.model, "prompt": prompt, "stream": False},
                    timeout=60.0,
                )
                result = resp.json()
                return json.loads(result.get("response", "[]"))
            except (httpx.RequestError, json.JSONDecodeError):
                return []
