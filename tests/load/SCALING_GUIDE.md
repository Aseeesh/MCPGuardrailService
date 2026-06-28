# Scaling & Resource Guide

## Performance Targets

| Stage | Target | Description |
|-------|--------|-------------|
| Deterministic | <20ms p50, <50ms p95 | Regex, keywords, schema validation |
| OPA Policy | <30ms p50, <80ms p95 | Rego evaluation via OPA |
| LLM Judge | <3s p50, <5s p95 | Ollama inference (async) |
| Full Pipeline | <50ms p50 (deterministic only) | Without LLM stage |

## Resource Sizing

### Small (Dev/Testing)
- **Throughput**: ~100 req/s
- API Gateway: 1 CPU, 512MB RAM
- AI Service: 1 CPU, 1GB RAM
- OPA: 0.5 CPU, 256MB RAM
- PostgreSQL: 1 CPU, 512MB RAM
- Redis: 0.5 CPU, 256MB RAM
- Ollama: 2 CPU, 4GB RAM (CPU inference)
- **Est. Azure Cost**: ~$50/month (Free tier + B1ms)

### Medium (Staging)
- **Throughput**: ~1,000 req/s
- API Gateway: 2 CPU, 2GB RAM (2 replicas)
- AI Service: 2 CPU, 2GB RAM (2 replicas)
- OPA: 1 CPU, 512MB RAM (2 replicas)
- PostgreSQL: 2 CPU, 4GB RAM
- Redis: 1 CPU, 1GB RAM
- Ollama: 4 CPU, 8GB RAM or GPU
- **Est. Azure Cost**: ~$300/month

### Large (Production)
- **Throughput**: ~10,000 req/s
- API Gateway: 4 CPU, 4GB RAM (4 replicas, auto-scale)
- AI Service: 4 CPU, 4GB RAM (4 replicas)
- OPA: 2 CPU, 1GB RAM (3 replicas)
- PostgreSQL: 4 CPU, 16GB RAM (read replicas)
- Redis: 2 CPU, 4GB RAM (cluster)
- Ollama: GPU instance (A10G or T4)
- RabbitMQ: 2 CPU, 2GB RAM (cluster)
- **Est. Azure Cost**: ~$1,500/month

## Optimization Strategies

### 1. Deterministic Stage (<20ms)
- Regex compiled with `NonBacktracking` flag (no catastrophic backtracking)
- Early exit on critical violations (skip LLM stage)
- In-memory LRU cache (10K entries, 60s TTL)

### 2. Caching (L1 + L2)
- **L1**: In-memory ConcurrentDictionary LRU (60s TTL)
- **L2**: Redis with 5-minute TTL
- Cache key includes content hash + policy version
- Invalidation on policy version change

### 3. Async Processing
- LLM checks queued to RabbitMQ `guardrail.llm-judge`
- Audit logging queued to `guardrail.audit-log`
- Immediate deterministic response, background LLM enrichment
- Webhook callback on LLM completion

### 4. Database
- BRIN index on `audit_log.timestamp` for time-range queries
- Partial indexes for pending reviews and escalations
- Composite indexes for tenant+action+time queries
- Connection pooling (200 max connections)
- Monthly partitioning for audit_log (production)

### 5. Circuit Breaker
- LLM calls protected: 5 failures → open (30s recovery)
- Pipeline degrades gracefully to deterministic-only mode

## Load Test Scenarios

```bash
# Run full suite
./tests/load/run_load_tests.sh

# Individual tests
k6 run tests/load/k6_deterministic.js    # Deterministic benchmarks
k6 run tests/load/k6_stress.js           # Stress & spike tests
```

## Monitoring

```bash
# Start with monitoring stack
docker compose --profile monitoring up -d

# Access dashboards
# Grafana: http://localhost:3000 (admin/admin)
# Prometheus: http://localhost:9090
# API Metrics: http://localhost:5000/api/metrics
# Prometheus format: http://localhost:5000/api/metrics/prometheus
```
