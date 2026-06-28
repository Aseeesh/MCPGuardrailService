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
| LLM | Ollama (Llama 2 / Mistral) |
| Policy Engine | Open Policy Agent (OPA) with Rego |
| MCP Servers | Azure MCP, Dawnguard MCP, Custom |
| Database | PostgreSQL 16 |
| Cache | Redis 7 |
| Messaging | RabbitMQ 3.13 |
| IaC | Terraform (Azure Free Tier) |
| Container | Docker Compose |

## Core Features

- **MCP Integration**: Azure MCP Server (200+ tools), Dawnguard MCP for security insights, custom guardrail tools
- **AI Agent Architecture**: Autonomous compliance agents via LangGraph with MCP-based tool calling
- **Detection Pipeline**: Deterministic rules + LLM-as-judge for content classification
- **Remediation Actions**: Redact, rewrite, block, escalate
- **Compliance Controls**: GDPR, HIPAA, SOC2
- **Audit Trail**: Full PostgreSQL-backed audit logging

## Quick Start

```bash
# Clone and start all services
docker compose up -d

# Pull Ollama model
docker exec -it ollama ollama pull mistral

# Access services
# Frontend:  http://localhost:5173
# API:       http://localhost:5000
# AI Service: http://localhost:8000
# OPA:       http://localhost:8181
# RabbitMQ:  http://localhost:15672
```

## Project Structure

```
MCPGuardrailService/
├── docker-compose.yml
├── terraform/
│   └── main.tf
├── src/
│   ├── api-gateway/          # .NET 9 C# API Gateway
│   │   ├── GuardrailApi.csproj
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   │   ├── ComplianceController.cs
│   │   │   └── GuardrailController.cs
│   │   └── Dockerfile
│   ├── ai-service/           # Python FastAPI + LangGraph
│   │   ├── main.py
│   │   ├── agents/
│   │   │   └── compliance_agent.py
│   │   ├── mcp_tools/
│   │   │   ├── __init__.py
│   │   │   ├── guardrail_tools.py
│   │   │   └── mcp_config.json
│   │   ├── requirements.txt
│   │   └── Dockerfile
│   ├── policy-engine/        # OPA Rego Policies
│   │   └── policies/
│   │       ├── gdpr.rego
│   │       ├── hipaa.rego
│   │       └── soc2.rego
│   └── frontend/             # React 19.2 + Vite
│       ├── package.json
│       ├── vite.config.ts
│       ├── tsconfig.json
│       ├── index.html
│       ├── src/
│       │   ├── App.tsx
│       │   ├── main.tsx
│       │   └── components/
│       │       └── Dashboard.tsx
│       └── Dockerfile
└── README.md
```

## License

MIT
