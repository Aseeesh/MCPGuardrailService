#!/bin/bash
set -e

# Canary deployment script for policy rollout
# Usage: ./canary_deploy.sh <rollout_pct> <opa_url> [--rollback]

ROLLOUT_PCT="${1:-1}"
OPA_URL="${2:-http://localhost:8181}"
ROLLBACK="${3:-}"
POLICY_DIR="$(cd "$(dirname "$0")/.." && pwd)/policies"
VERSION_FILE="/tmp/policy_version_$(echo "$OPA_URL" | md5sum | cut -c1-8)"

echo "=== Policy Canary Deployment ==="
echo "OPA: $OPA_URL"
echo "Rollout: ${ROLLOUT_PCT}%"
echo ""

# Rollback mode
if [ "$ROLLBACK" = "--rollback" ]; then
    if [ -f "$VERSION_FILE.prev" ]; then
        PREV_VERSION=$(cat "$VERSION_FILE.prev")
        echo "Rolling back to: $PREV_VERSION"
        git checkout "$PREV_VERSION" -- "$POLICY_DIR" 2>/dev/null || {
            echo "ERROR: Cannot rollback — previous version not found in git"
            exit 1
        }
        echo "Reloading previous policies..."
    else
        echo "ERROR: No previous version recorded"
        exit 1
    fi
fi

# Compute current version hash
CURRENT_HASH=$(find "$POLICY_DIR" -name "*.rego" ! -name "*_test*" -exec sha256sum {} \; | sha256sum | cut -c1-16)
echo "Policy hash: $CURRENT_HASH"

# Save previous version for rollback
[ -f "$VERSION_FILE" ] && cp "$VERSION_FILE" "$VERSION_FILE.prev"
echo "$CURRENT_HASH" > "$VERSION_FILE"

# Deploy policies
echo ""
echo "--- Loading Policies ---"
LOADED=0
ERRORS=0
find "$POLICY_DIR" -name "*.rego" ! -name "*_test*" | while read f; do
    REL_PATH="${f#$POLICY_DIR/}"
    POLICY_ID=$(echo "$REL_PATH" | sed 's|/|.|g;s|\.rego||')

    HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" \
        -X PUT "$OPA_URL/v1/policies/$POLICY_ID" \
        -H "Content-Type: text/plain" \
        --data-binary "@$f")

    if [ "$HTTP_CODE" = "200" ]; then
        echo "  ✓ $POLICY_ID"
        LOADED=$((LOADED + 1))
    else
        echo "  ✗ $POLICY_ID (HTTP $HTTP_CODE)"
        ERRORS=$((ERRORS + 1))
    fi
done

if [ $ERRORS -gt 0 ]; then
    echo ""
    echo "ERROR: $ERRORS policies failed to load. Aborting canary."
    exit 1
fi

# Monitor phase (if canary)
if [ "$ROLLOUT_PCT" -le 10 ]; then
    echo ""
    echo "--- Canary Monitoring (60s) ---"
    MONITOR_SECONDS=60
    INTERVAL=10

    for i in $(seq 1 $((MONITOR_SECONDS / INTERVAL))); do
        sleep $INTERVAL

        # Check OPA health
        HEALTH=$(curl -s -o /dev/null -w "%{http_code}" "$OPA_URL/health")
        if [ "$HEALTH" != "200" ]; then
            echo "  ✗ OPA unhealthy (HTTP $HEALTH) — triggering rollback"
            exec "$0" "$ROLLOUT_PCT" "$OPA_URL" --rollback
        fi

        echo "  [$((i * INTERVAL))s] OPA healthy, policies active"
    done

    echo ""
    echo "✓ Canary passed — safe to increase rollout"
fi

echo ""
echo "=== Deployment Complete ==="
echo "Version: $CURRENT_HASH"
echo "Rollout: ${ROLLOUT_PCT}%"
