"""LLM-as-Judge integration using Ollama for policy evaluation."""

import json
import httpx
from prompts.llm_judge_templates import get_judge_prompt, SYSTEM_PROMPT


class LlmJudge:
    def __init__(self, ollama_url: str, model: str = "mistral"):
        self.ollama_url = ollama_url
        self.model = model

    async def judge(
        self,
        content: str,
        policy_type: str,
        frameworks: list[str] | None = None,
        resource_type: str = "text",
        source: str = "user_input",
        existing_violations: list[dict] | None = None,
    ) -> dict:
        prompt = get_judge_prompt(
            policy_type=policy_type,
            content=content,
            frameworks=frameworks,
            resource_type=resource_type,
            source=source,
            existing_violations=existing_violations,
        )

        async with httpx.AsyncClient() as client:
            try:
                resp = await client.post(
                    f"{self.ollama_url}/api/generate",
                    json={
                        "model": self.model,
                        "system": SYSTEM_PROMPT,
                        "prompt": prompt,
                        "stream": False,
                        "options": {
                            "temperature": 0.1,
                            "top_p": 0.9,
                            "num_predict": 2048,
                        },
                    },
                    timeout=60.0,
                )
                raw = resp.json().get("response", "{}")
                return self._parse_response(raw, policy_type)
            except (httpx.RequestError, json.JSONDecodeError) as e:
                return {
                    "violations": [],
                    "reasoning": f"LLM judge unavailable: {e}",
                    "error": True,
                }

    async def judge_all_types(
        self,
        content: str,
        frameworks: list[str],
        resource_type: str = "text",
        source: str = "user_input",
    ) -> dict:
        """Run all relevant judge types in parallel."""
        types_to_check = self._determine_judge_types(frameworks, source)

        import asyncio
        tasks = [
            self.judge(content, pt, frameworks, resource_type, source)
            for pt in types_to_check
        ]
        results = await asyncio.gather(*tasks, return_exceptions=True)

        all_violations = []
        all_reasoning = []
        for pt, result in zip(types_to_check, results):
            if isinstance(result, Exception):
                continue
            all_violations.extend(result.get("violations", []))
            reasoning = result.get("reasoning", "")
            if reasoning:
                all_reasoning.append(f"[{pt}] {reasoning}")

        return {
            "violations": all_violations,
            "reasoning": " | ".join(all_reasoning),
            "types_checked": types_to_check,
        }

    def _parse_response(self, raw: str, policy_type: str) -> dict:
        try:
            start = raw.find("{")
            end = raw.rfind("}") + 1
            if start >= 0 and end > start:
                return json.loads(raw[start:end])
        except json.JSONDecodeError:
            pass

        return {
            "violations": [],
            "reasoning": f"Failed to parse LLM response for {policy_type}",
            "raw": raw[:500],
        }

    def _determine_judge_types(self, frameworks: list[str], source: str) -> list[str]:
        types = []
        if any(f in frameworks for f in ["GDPR", "HIPAA"]):
            types.append("pii")
        if source in ("user_input", "api_request"):
            types.append("security")
        if frameworks:
            types.append("compliance")
        types.append("safety")
        return types
