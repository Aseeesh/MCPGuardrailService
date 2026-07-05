# MCP-Enabled Guardrail & Compliance Service

An enterprise-grade AI guardrail and compliance service leveraging Model Context Protocol (MCP) for cloud-native security, policy enforcement, and automated remediation.

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                        React 19.2 + Vite Frontend               │
│                    (Policy Dashboard / Audit Viewer)             │
└──────────────────────────────┬──────────────────────────────────┘
                               │ REST/WebSocket
┌──────────────────────────────▼──────────────────────────────────┐
│                    .NET 9 API Gateway (C#)                       │
│              Authentication · Rate Limiting · Routing            │
└───────┬──────────────────┬──────────────────┬───────────────────┘
        │                  │                  │
┌───────▼───────┐  ┌───────▼───────┐  ┌───────▼───────┐
│  Python AI    │  │   Policy      │  │  Compliance   │
│  Service      │  │   Engine      │  │  Scanner      │
│  (FastAPI)    │  │   (OPA/Rego)  │  │  (MCP-based)  │
│  LangGraph    │  │               │  │               │
│  Agents       │  │               │  │               │
└───────┬───────┘  └───────┬───────┘  └───────┬───────┘
        │                  │                  │
┌───────▼──────────────────▼──────────────────▼───────────────────┐
│                     MCP Integration Layer                        │
│  ┌─────────────┐  ┌──────────────┐  ┌────────────────────┐      │
│  │ Azure MCP   │  │ Dawnguard    │  │ Custom Guardrail   │      │
│  │ Server      │  │ MCP          │  │ MCP Tools          │      │
│  │ (200+ tools)│  │ (Security)   │  │                    │      │
│  └─────────────┘  └──────────────┘  └────────────────────┘      │
└───────┬──────────────────┬──────────────────┬───────────────────┘
        │                  │                  │
┌───────▼───────┐  ┌───────▼───────┐  ┌───────▼───────┐
│  PostgreSQL   │  │    Redis      │  │  RabbitMQ     │
│  (Audit)      │  │   (Cache)     │  │  (Messaging)  │
└───────────────┘  └───────────────┘  └───────────────┘
```

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Frontend | React 19.2, Vite, TypeScript |
| API Gateway | C# .NET 9 |
| AI Services | Python 3.12, FastAPI, LangGraph |
| LLM | Ollama (Mistral) — runs locally via Docker |
| Policy Engine | Open Policy Agent (OPA) with Rego |
| MCP Servers | Azure MCP, Dawnguard MCP, Custom |
| Database | PostgreSQL 16 |
| Cache | Redis 7 |
| Messaging | RabbitMQ 3.13 |
| IaC | Terraform (Azure Free Tier) |
| Container | Docker Compose |

---

## Local Setup & Running

### Prerequisites

- **Docker Desktop** ≥ 4.x with at least 8 GB RAM allocated  
  _(Ollama + all services require ~6 GB)_
- **Node.js** 22+ (for local frontend dev outside Docker)
- **make** (pre-installed on macOS/Linux)

---

### Step 1 — Clone and configure environment

```bash
git clone <repo-url>
cd MCPGuardrailService

# Copy and configure environment variables
cp .env.example .env
# Edit .env if you want custom passwords (defaults work for local dev)
```

The `.env` file sets:
| Variable | Default | Purpose |
|----------|---------|---------|
| `POSTGRES_PASSWORD` | `guardrail_dev` | PostgreSQL password |
| `RABBITMQ_PASSWORD` | `guardrail_dev` | RabbitMQ password |
| `GRAFANA_PASSWORD` | `admin` | Grafana admin password |

---

### Step 2 — Build and start all services

```bash
make up
```

This builds all Docker images and starts:

| Service | URL | Notes |
|---------|-----|-------|
| **Frontend** | http://localhost:5174 | React 19.2 dashboard |
| **API Gateway** | http://localhost:5001 | .NET 9 REST API |
| **AI Service** | http://localhost:8000/docs | FastAPI + Swagger UI |
| **OPA** | http://localhost:8181 | Policy engine |
| **RabbitMQ UI** | http://localhost:15672 | User: `guardrail` / Pass: `guardrail_dev` |
| **PostgreSQL** | localhost:5432 | DB: `guardrail` |
| **Redis** | localhost:6379 | |
| **Ollama** | http://localhost:11434 | LLM runtime |

Wait ~30 seconds for all services to finish their health checks.

---

### Step 3 — Pull the Mistral LLM model

This is required for LLM-as-Judge and rewrite features. Run once after `make up`:

```bash
make pull-model
```

> Download is ~4 GB. Progress is shown in the terminal.  
> To use Llama 3.2 instead: `make pull-model-llama`

---

### Step 4 — Load policies into OPA

Load all Rego policy files into the running OPA instance:

```bash
make load-policies
```

OPA also hot-reloads from the mounted `./src/policy-engine/policies` directory, so edits to `.rego` files take effect immediately without restart.

---

### Step 5 — Verify everything is running

```bash
make status
```

Expected output:
```
=== Service Health ===
── Containers ──
NAME                   STATUS        PORTS
...api-gateway         Up (healthy)  0.0.0.0:5001->8080/tcp
...ai-service          Up            0.0.0.0:8000->8000/tcp
...frontend            Up            0.0.0.0:5174->5174/tcp
...opa                 Up            0.0.0.0:8181->8181/tcp
...postgres            Up (healthy)  0.0.0.0:5432->5432/tcp
...redis               Up (healthy)  0.0.0.0:6379->6379/tcp
...rabbitmq            Up (healthy)  ...
...ollama              Up            0.0.0.0:11434->11434/tcp

── API Gateway health ──
{"status":"healthy"}

── AI Service health ──
{"status":"healthy"}

── OPA policies loaded ──
  6 policies loaded
```

---

## Testing the Full Functionality

### Quick — run all tests

```bash
make test
```

Runs OPA unit tests + API smoke tests in sequence.

---

### Test: OPA Policy Unit Tests

```bash
make test-policies
```

Runs the Rego test suite (GDPR/HIPAA/injection tests) using `opa test`. Expected: 20+ tests passing.

---

### Test: API Smoke Tests

```bash
make test-api
```

Runs a sequence of curl calls covering:
1. Health checks on API Gateway and AI Service
2. Clean content → expect `"decision": "allow"`
3. PII content (SSN + email) → expect `"decision": "block"` with violations
4. SQL injection content → expect injection violations
5. Full LangGraph compliance workflow
6. Policy status from OPA
7. Review queue stats

---

### Test: LLM-as-Judge (requires Mistral)

```bash
make test-llm
```

Sends content through the Mistral LLM judge for semantic analysis. Requires `make pull-model` to have completed.

---

### Test: Rewrite Engine

```bash
make test-rewrite
```

Sends overly promotional content through the LLM rewrite engine. Expect a toned-down version back.

---

### Test: Human Review Queue

```bash
make test-review-queue
```

Submits a HIPAA PHI item for human review and fetches the pending queue.

---

### Manual testing via Swagger UI

Open **http://localhost:8000/docs** in your browser for the full interactive AI Service API:

Key endpoints to try manually:

| Endpoint | Method | What it does |
|----------|--------|--------------|
| `/api/validate` | POST | Basic PII + policy check |
| `/api/policies/evaluate` | POST | Full multi-policy OPA evaluation |
| `/api/workflow/run` | POST | Full LangGraph agent workflow |
| `/api/llm/judge` | POST | Semantic LLM analysis |
| `/api/rewrite` | POST | LLM content rewrite |
| `/api/review/submit` | POST | Submit for human review |
| `/api/review/pending` | GET | List pending reviews |
| `/api/policies/status` | GET | OPA policy inventory |
| `/api/policies/reload` | POST | Hot-reload policies |

---

### Test via the React Dashboard

Open **http://localhost:5174** and use the 5 tabs:

| Tab | What to test |
|-----|-------------|
| **Dashboard** | Live metrics, detection stats |
| **Validate** | Paste text → see real-time policy evaluation |
| **Compliance** | GDPR/HIPAA/SOC2 assessment scores |
| **Review Queue** | Human-in-the-loop review items |
| **Audit** | Immutable audit log with hash chain |

---

## Common Commands

```bash
make up              # Start all services
make down            # Stop all services
make restart         # Restart all services
make pull-model      # Download Mistral LLM (first time)
make load-policies   # Load Rego policies into OPA
make status          # Health check all services
make test            # Run all tests
make test-api        # API smoke tests only
make test-policies   # OPA unit tests only
make test-llm        # LLM judge test (needs Mistral)
make logs            # Stream all service logs
make logs-api        # API Gateway logs only
make logs-ai         # AI Service logs only
make monitoring      # Start with Prometheus + Grafana
make clean           # Remove containers + volumes
make reset           # Full reset including images
```

---

## Optional: Start with Monitoring Stack

```bash
make monitoring
```

Adds Prometheus and Grafana:

| Service | URL |
|---------|-----|
| Prometheus | http://localhost:9090 |
| Grafana | http://localhost:3000 (admin / admin) |

Grafana is pre-configured with the Guardrail Overview dashboard.

---

## Troubleshooting

### Services take too long to start
Docker needs time to pull images on first run. Run `docker compose ps` to check status. API Gateway has a health dependency on PostgreSQL — it starts only after Postgres is healthy.

### Ollama runs out of memory
Reduce Docker Desktop memory to check: open Docker Desktop → Settings → Resources. Ollama + Mistral needs ~4 GB. If constrained, use `llama3.2:1b` instead:
```bash
docker exec -it ollama ollama pull llama3.2:1b
```

### Frontend shows blank screen
```bash
make logs-frontend   # Check for build errors
docker compose restart frontend
```

### OPA has 0 policies loaded
```bash
make load-policies   # Push policies via REST
# or restart OPA to trigger hot-reload from mounted volume
docker compose restart opa
```

### Reset everything and start fresh
```bash
make reset
make up
make pull-model
make load-policies
```

---

## Project Structure

```
MCPGuardrailService/
├── Makefile                        # All local dev commands
├── docker-compose.yml              # Full stack (8 services)
├── .env                            # Local dev secrets (git-ignored)
├── .env.example                    # Template for .env
├── src/
│   ├── api-gateway/                # .NET 9 C# API Gateway
│   │   ├── GuardrailApi.csproj
│   │   ├── Program.cs
│   │   ├── Pipeline/               # Detection pipeline + ensemble
│   │   ├── Security/               # JWT, RBAC, rate limiting
│   │   ├── Audit/                  # Immutable audit trail
│   │   ├── Compliance/             # GDPR/HIPAA/SOC2 controls
│   │   ├── Performance/            # Redis cache, RabbitMQ, metrics
│   │   └── Dockerfile
│   ├── ai-service/                 # Python FastAPI + LangGraph
│   │   ├── main.py                 # 30+ endpoints
│   │   ├── agents/                 # LangGraph compliance agents
│   │   ├── pipeline/               # LLM-as-Judge
│   │   ├── remediation/            # Rewrite engine + review queue
│   │   ├── mcp_tools/              # MCP tool definitions
│   │   └── Dockerfile
│   ├── policy-engine/              # OPA Rego Policies
│   │   ├── policies/
│   │   │   ├── pii/                # GDPR, HIPAA, SOC2 PII rules
│   │   │   ├── security/           # SQL injection, XSS, command injection
│   │   │   ├── safety/             # Content safety, self-harm escalation
│   │   │   └── brand/              # Tone, prohibited language
│   │   ├── tests/                  # Rego unit tests
│   │   └── templates/              # Policy YAML templates
│   └── frontend/                   # React 19.2 + Vite
│       ├── src/
│       │   ├── App.tsx             # 5-tab shell
│       │   └── components/
│       │       ├── Dashboard.tsx
│       │       ├── review/         # Human review queue UI
│       │       ├── audit/          # Audit log + hash chain UI
│       │       └── compliance/     # GDPR/HIPAA/SOC2 dashboard
│       └── Dockerfile
├── sql/
│   ├── 001_audit_schema.sql        # Immutable audit tables + triggers
│   └── 002_performance_indexes.sql
├── monitoring/
│   ├── prometheus/prometheus.yml
│   └── grafana/dashboards/
├── terraform/                      # Azure Free Tier IaC
├── docs/
│   ├── case-study/CASE_STUDY.md
│   ├── diagrams/                   # Mermaid architecture diagrams
│   ├── metrics/                    # Benchmarks, ROI, regulatory mapping
│   └── BLOG_POST.md
└── scripts/
    ├── readiness/                  # Production readiness checklist
    └── deploy.sh
```

---

## License

MIT
