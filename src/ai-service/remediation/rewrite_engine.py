"""
Rewrite engine using Ollama for content remediation.

Supports:
- Tone adjustment (professional, friendly, neutral)
- Uncertainty addition for risky claims
- JSON repair for malformed output
- Confidence-based rewrites
"""

import json
import httpx


REWRITE_TEMPLATES = {
    "tone_professional": """Rewrite the following content in a professional, business-appropriate tone.
Preserve all factual information. Remove casual language, slang, and inappropriate phrasing.

Original:
{content}

Violations found:
{violations}

Return ONLY the rewritten content.""",

    "tone_friendly": """Rewrite the following content in a warm, friendly, approachable tone.
Preserve all factual information. Make it conversational but respectful.

Original:
{content}

Return ONLY the rewritten content.""",

    "add_uncertainty": """The following content makes claims that may be inaccurate or risky.
Add appropriate hedging language and disclaimers without changing the core message.

Rules:
- Prefix definitive claims with "Based on available information..." or "It appears that..."
- Add "Please verify this information" for factual claims
- Add domain-specific disclaimers if the content is medical, financial, or legal

Original:
{content}

Risky claims identified:
{violations}

Return ONLY the rewritten content with appropriate uncertainty markers.""",

    "remove_pii": """Remove all personally identifiable information from this content.
Replace each PII element with a descriptive placeholder:
- Names → [Person Name]
- Emails → [Email Address]
- Phone numbers → [Phone Number]
- Addresses → [Physical Address]
- SSN/ID numbers → [ID Number]
- Medical info → [Medical Information]

Preserve the structure and non-PII content exactly.

Original:
{content}

PII detected:
{violations}

Return ONLY the content with PII replaced by placeholders.""",

    "compliance_safe": """Rewrite this content to be compliant with {frameworks} regulations.

Rules:
- Remove any direct references to protected data
- Generalize specific examples that contain real data
- Add required disclaimers for the applicable frameworks
- Maintain the original intent and usefulness

Original:
{content}

Violations:
{violations}

Return ONLY the compliant version.""",

    "json_repair": """The following content is malformed JSON. Fix it to be valid JSON.

Rules:
- Fix missing quotes, brackets, commas
- Do NOT change the data values
- Preserve the intended structure
- If ambiguous, prefer the most common JSON conventions

Malformed content:
{content}

Return ONLY the repaired valid JSON.""",
}


class RewriteEngine:
    def __init__(self, ollama_url: str, model: str = "mistral"):
        self.ollama_url = ollama_url
        self.model = model

    async def rewrite(
        self,
        content: str,
        rewrite_type: str,
        violations: list[dict] | None = None,
        frameworks: list[str] | None = None,
    ) -> dict:
        template = REWRITE_TEMPLATES.get(rewrite_type)
        if not template:
            return {"error": f"Unknown rewrite type: {rewrite_type}", "modified_content": content}

        prompt = template.format(
            content=content,
            violations=json.dumps(violations or [], indent=2),
            frameworks=", ".join(frameworks or []),
        )

        rewritten = await self._call_llm(prompt)

        if rewrite_type == "json_repair" and rewritten:
            try:
                json.loads(rewritten)
            except json.JSONDecodeError:
                return {
                    "action": "json_repair",
                    "modified_content": content,
                    "explanation": "JSON repair failed — original content preserved",
                    "success": False,
                }

        return {
            "action": rewrite_type,
            "modified_content": rewritten or content,
            "explanation": f"Content rewritten using '{rewrite_type}' template",
            "success": rewritten is not None,
        }

    async def auto_select_and_rewrite(
        self,
        content: str,
        violations: list[dict],
        frameworks: list[str] | None = None,
    ) -> dict:
        """Automatically select the best rewrite strategy based on violations."""
        violation_types = {v.get("type", "") for v in violations}

        if any("pii" in t or "phi" in t for t in violation_types):
            return await self.rewrite(content, "remove_pii", violations, frameworks)

        if any("brand" in t or "tone" in t for t in violation_types):
            return await self.rewrite(content, "tone_professional", violations, frameworks)

        if any("compliance" in t for t in violation_types):
            return await self.rewrite(content, "compliance_safe", violations, frameworks)

        if content.lstrip().startswith(("{", "[")):
            try:
                json.loads(content)
            except json.JSONDecodeError:
                return await self.rewrite(content, "json_repair", violations)

        return await self.rewrite(content, "add_uncertainty", violations, frameworks)

    async def _call_llm(self, prompt: str) -> str | None:
        async with httpx.AsyncClient() as client:
            try:
                resp = await client.post(
                    f"{self.ollama_url}/api/generate",
                    json={
                        "model": self.model,
                        "prompt": prompt,
                        "stream": False,
                        "options": {"temperature": 0.3},
                    },
                    timeout=60.0,
                )
                return resp.json().get("response")
            except (httpx.RequestError, KeyError):
                return None
