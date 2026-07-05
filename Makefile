.PHONY: help setup up down restart logs pull-model status test test-api test-policies clean reset

# Default target
help:
	@echo ""
	@echo "MCP Guardrail Service — Local Development"
	@echo "==========================================="
	@echo ""
	@echo "  make setup        First-time setup (copy .env, install frontend deps)"
	@echo "  make up           Start all services"
	@echo "  make down         Stop all services"
	@echo "  make restart      Restart all services"
	@echo "  make pull-model   Pull Mistral LLM into Ollama (run after 'make up')"
	@echo "  make logs         Tail logs from all services"
	@echo "  make logs-api     Tail API gateway logs"
	@echo "  make logs-ai      Tail AI service logs"
	@echo "  make status       Show health status of all services"
	@echo "  make test         Run all tests (API + policy unit tests)"
	@echo "  make test-api     Run API smoke tests"
	@echo "  make test-policies Run OPA policy unit tests"
	@echo "  make monitoring   Start with Prometheus + Grafana"
	@echo "  make clean        Remove containers and volumes"
	@echo "  make reset        Full reset — remove everything including images"
	@echo ""

# ── Setup ────────────────────────────────────────────────────────────────────

setup:
	@echo "→ Setting up environment..."
	@if [ ! -f .env ]; then cp .env.example .env && echo "  Created .env from .env.example — edit passwords if needed"; fi
	@echo "→ Installing frontend dependencies..."
	cd src/frontend && npm install
	@echo ""
	@echo "✓ Setup complete. Run 'make up' to start all services."

# ── Docker lifecycle ─────────────────────────────────────────────────────────

up:
	@echo "→ Starting all services..."
	docker compose up -d --build
	@echo ""
	@echo "✓ Services started. Run 'make pull-model' to download Mistral (first time only)."
	@echo ""
	@echo "  Frontend:   http://localhost:5174"
	@echo "  API:        http://localhost:5001"
	@echo "  AI Service: http://localhost:8000/docs"
	@echo "  OPA:        http://localhost:8181"
	@echo "  RabbitMQ:   http://localhost:15672  (guest/guardrail_dev)"
	@echo ""

down:
	docker compose down

restart:
	docker compose restart

logs:
	docker compose logs -f

logs-api:
	docker compose logs -f api-gateway

logs-ai:
	docker compose logs -f ai-service

logs-frontend:
	docker compose logs -f frontend

monitoring:
	docker compose --profile monitoring up -d
	@echo "  Prometheus: http://localhost:9090"
	@echo "  Grafana:    http://localhost:3000  (admin/admin)"

# ── Ollama ───────────────────────────────────────────────────────────────────

pull-model:
	@echo "→ Pulling Mistral model into Ollama (this takes a few minutes)..."
	docker exec -it ollama ollama pull mistral
	@echo "✓ Mistral model ready."

pull-model-llama:
	@echo "→ Pulling Llama 3.2 model..."
	docker exec -it ollama ollama pull llama3.2
	@echo "✓ Llama 3.2 ready."

list-models:
	docker exec ollama ollama list

# ── Health / Status ──────────────────────────────────────────────────────────

status:
	@echo "=== Service Health ==="
	@echo ""
	@echo "── Containers ──"
	@docker compose ps --format "table {{.Name}}\t{{.Status}}\t{{.Ports}}"
	@echo ""
	@echo "── API Gateway health ──"
	@curl -sf http://localhost:5001/health 2>/dev/null && echo "" || echo "  ✗ API Gateway not responding (is it started?)"
	@echo ""
	@echo "── AI Service health ──"
	@curl -sf http://localhost:8000/health 2>/dev/null && echo "" || echo "  ✗ AI Service not responding"
	@echo ""
	@echo "── OPA policies loaded ──"
	@curl -sf http://localhost:8181/v1/policies 2>/dev/null | python3 -c "import sys,json; p=json.load(sys.stdin).get('result',[]); print(f'  {len(p)} policies loaded')" 2>/dev/null || echo "  ✗ OPA not responding"
	@echo ""

# ── Tests ────────────────────────────────────────────────────────────────────

test: test-policies test-api
	@echo ""
	@echo "✓ All tests complete."

test-api:
	@echo "=== API Smoke Tests ==="
	@echo ""
	@echo "── Health checks ──"
	@curl -sf http://localhost:5001/health && echo " ✓ API Gateway" || echo " ✗ API Gateway"
	@curl -sf http://localhost:8000/health && echo " ✓ AI Service" || echo " ✗ AI Service"
	@echo ""
	@echo "── Validate clean content ──"
	@curl -sf -X POST http://localhost:8000/api/validate \
		-H "Content-Type: application/json" \
		-d '{"content":"Hello world, this is a test message.","resource_type":"text","frameworks":["GDPR"]}' \
		| python3 -m json.tool 2>/dev/null || echo "  (AI service not ready)"
	@echo ""
	@echo "── Validate PII content ──"
	@curl -sf -X POST http://localhost:8000/api/validate \
		-H "Content-Type: application/json" \
		-d '{"content":"My SSN is 123-45-6789 and email is john@example.com","resource_type":"text","frameworks":["GDPR","HIPAA"]}' \
		| python3 -m json.tool 2>/dev/null || echo "  (AI service not ready)"
	@echo ""
	@echo "── Policy evaluation ──"
	@curl -sf -X POST http://localhost:8000/api/policies/evaluate \
		-H "Content-Type: application/json" \
		-d '{"content":"SELECT * FROM users WHERE id = 1 OR 1=1","resource_type":"query","frameworks":["SOC2"]}' \
		| python3 -m json.tool 2>/dev/null || echo "  (AI service not ready)"
	@echo ""
	@echo "── LangGraph workflow ──"
	@curl -sf -X POST http://localhost:8000/api/workflow/run \
		-H "Content-Type: application/json" \
		-d '{"content":"Process this customer record for John Smith at john@example.com","resource_type":"text","frameworks":["GDPR"],"source":"user_input"}' \
		| python3 -m json.tool 2>/dev/null || echo "  (AI service not ready)"
	@echo ""
	@echo "── Policy status (OPA) ──"
	@curl -sf http://localhost:8000/api/policies/status | python3 -m json.tool 2>/dev/null || echo "  (OPA not ready)"
	@echo ""
	@echo "── Review queue stats ──"
	@curl -sf http://localhost:8000/api/review/stats | python3 -m json.tool 2>/dev/null || echo "  (AI service not ready)"
	@echo ""

test-policies:
	@echo "=== OPA Policy Unit Tests ==="
	@docker run --rm \
		-v "$(PWD)/src/policy-engine/policies:/policies:ro" \
		-v "$(PWD)/src/policy-engine/tests:/tests:ro" \
		openpolicyagent/opa:latest test /policies /tests -v 2>&1
	@echo ""

test-llm:
	@echo "=== LLM Judge Test ==="
	@echo "Sending content through LLM-as-Judge (requires Mistral to be pulled)..."
	@curl -sf -X POST http://localhost:8000/api/llm/judge \
		-H "Content-Type: application/json" \
		-d '{"content":"You must click this link immediately or your account will be terminated.","resource_type":"text","frameworks":["SOC2"]}' \
		| python3 -m json.tool 2>/dev/null || echo "  (LLM service not ready — run make pull-model first)"
	@echo ""

test-rewrite:
	@echo "=== Rewrite Engine Test ==="
	@curl -sf -X POST http://localhost:8000/api/rewrite \
		-H "Content-Type: application/json" \
		-d '{"content":"Call us now! Best deal ever!! Limited time offer!!!","violations":[],"frameworks":["SOC2"]}' \
		| python3 -m json.tool 2>/dev/null || echo "  (AI service not ready)"
	@echo ""

test-review-queue:
	@echo "=== Review Queue Test ==="
	@echo "→ Submitting item for human review..."
	@REVIEW_ID=$$(curl -sf -X POST http://localhost:8000/api/review/submit \
		-H "Content-Type: application/json" \
		-d '{"content":"Patient ID 12345 has diagnosis code Z00.00","decision":"escalate","confidence":0.65,"violations":[{"type":"phi","severity":"high","description":"PHI detected"}],"reason":"PHI requires human review","frameworks":["HIPAA"]}' \
		| python3 -c "import sys,json; print(json.load(sys.stdin)['id'])" 2>/dev/null); \
	echo "  Submitted with ID: $$REVIEW_ID"; \
	echo "→ Fetching pending reviews..."; \
	curl -sf "http://localhost:8000/api/review/pending" | python3 -m json.tool 2>/dev/null | head -30; \
	echo ""

# ── Load Policies into OPA ───────────────────────────────────────────────────

load-policies:
	@echo "→ Loading Rego policies into OPA..."
	@curl -sf -X POST http://localhost:8000/api/policies/reload | python3 -m json.tool
	@echo ""

# ── Cleanup ───────────────────────────────────────────────────────────────────

clean:
	@echo "→ Stopping and removing containers + volumes..."
	docker compose down -v
	@echo "✓ Cleaned."

reset: clean
	@echo "→ Removing built images..."
	docker compose down --rmi local
	@echo "✓ Full reset complete. Run 'make up' to rebuild from scratch."
