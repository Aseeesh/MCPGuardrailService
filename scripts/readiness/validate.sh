#!/bin/bash
set -e

PROJECT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
PASS=0
FAIL=0
WARN=0

check() {
    local name="$1"
    local file="$2"
    local pattern="$3"

    if [ -f "$PROJECT_DIR/$file" ]; then
        if [ -z "$pattern" ] || grep -q "$pattern" "$PROJECT_DIR/$file" 2>/dev/null; then
            echo "  ✓ $name"
            PASS=$((PASS + 1))
        else
            echo "  ⚠ $name (file exists but pattern not found)"
            WARN=$((WARN + 1))
        fi
    else
        echo "  ✗ $name ($file missing)"
        FAIL=$((FAIL + 1))
    fi
}

echo "=== Production Readiness Validation ==="
echo ""

echo "--- Security ---"
check "JWT Authentication" "src/api-gateway/Security/JwtAuthMiddleware.cs" "ValidateToken"
check "RBAC Middleware" "src/api-gateway/Security/SecurityHeadersMiddleware.cs" "RbacMiddleware"
check "Rate Limiting" "src/api-gateway/Security/RateLimitingMiddleware.cs" "SlidingWindow"
check "Security Headers" "src/api-gateway/Security/SecurityHeadersMiddleware.cs" "Strict-Transport-Security"
check "SQL Injection Rules" "src/policy-engine/policies/security/injection.rego" "sql_injection"
check "XSS Detection" "src/api-gateway/Pipeline/Stages/DeterministicStage.cs" "xss"
check "Credential Detection" "src/api-gateway/Pipeline/Stages/DeterministicStage.cs" "aws_key"
check "Key Vault Setup" "scripts/security_setup.sh" "keyvault"
check "TLS Configuration" "terraform/modules/redis/main.tf" "minimum_tls_version"

echo ""
echo "--- Performance ---"
check "Compiled Regex" "src/api-gateway/Pipeline/Stages/DeterministicStage.cs" "NonBacktracking"
check "Redis Cache (L1+L2)" "src/api-gateway/Performance/Caching/PolicyCache.cs" "LruEntry"
check "RabbitMQ Async" "src/api-gateway/Performance/Async/RabbitMqPublisher.cs" "BasicPublishAsync"
check "Database Indexes" "sql/002_performance_indexes.sql" "CREATE INDEX"
check "Response Compression" "src/api-gateway/Program.cs" "UseResponseCompression"
check "Connection Pooling" "src/api-gateway/Program.cs" "AddSingleton.*IConnectionMultiplexer"

echo ""
echo "--- Reliability ---"
check "Health Checks" "src/api-gateway/Program.cs" "MapHealthChecks"
check "Circuit Breaker" "src/api-gateway/Pipeline/CircuitBreaker/CircuitBreaker.cs" "HalfOpen"
check "Retry Policy" "src/api-gateway/Reliability/ResiliencePolicy.cs" "Exponential"
check "Graceful Shutdown" "src/api-gateway/Reliability/ResiliencePolicy.cs" "GracefulShutdownService"
check "Docker Health Checks" "docker-compose.yml" "healthcheck"
check "Timeout Config" "src/api-gateway/Reliability/ResiliencePolicy.cs" "timeoutSeconds"

echo ""
echo "--- Observability ---"
check "Structured Logging" "src/api-gateway/Observability/ObservabilitySetup.cs" "AddJsonConsole"
check "OpenTelemetry" "src/api-gateway/Observability/ObservabilitySetup.cs" "AddOpenTelemetry"
check "Request Logging" "src/api-gateway/Observability/ObservabilitySetup.cs" "RequestLoggingMiddleware"
check "Prometheus Metrics" "src/api-gateway/Performance/Monitoring/PerformanceMetrics.cs" "GetPrometheusMetrics"
check "Grafana Dashboard" "monitoring/grafana/dashboards/guardrail-overview.json" "Pipeline Latency"
check "App Insights" "terraform/modules/monitoring/main.tf" "application_insights"
check "Alerting Rules" "terraform/modules/monitoring/main.tf" "monitor_metric_alert"
check "Audit Trail" "src/api-gateway/Audit/AuditService.cs" "ComputeRecordHash"
check "Budget Alerts" "terraform/main.tf" "consumption_budget"

echo ""
echo "--- Infrastructure ---"
check "Terraform Modules" "terraform/main.tf" "module"
check "CI/CD Pipeline" ".github/workflows/deploy-azure.yml" "Terraform"
check "Policy CI/CD" ".github/workflows/policy-deploy.yml" "Canary"
check "Docker Compose" "docker-compose.yml" "services"
check "Cost Estimation" "scripts/cost_estimation.yaml" "free_tier"

echo ""
echo "=================================="
TOTAL=$((PASS + FAIL + WARN))
echo "Results: $PASS passed, $WARN warnings, $FAIL failed (out of $TOTAL checks)"
SCORE=$((PASS * 100 / TOTAL))
echo "Readiness Score: ${SCORE}%"
echo ""

if [ $FAIL -gt 0 ]; then
    echo "Status: NOT READY — fix $FAIL failed checks"
    exit 1
elif [ $WARN -gt 3 ]; then
    echo "Status: CONDITIONAL — review $WARN warnings"
    exit 0
else
    echo "Status: READY FOR PRODUCTION"
    exit 0
fi
