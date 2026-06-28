#!/bin/bash
set -e

OPA_URL="${OPA_URL:-http://localhost:8181}"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
POLICY_DIR="$(dirname "$SCRIPT_DIR")/policies"

echo "=== Hot Reload Policies ==="
echo "OPA: $OPA_URL"
echo ""

find "$POLICY_DIR" -name "*.rego" ! -name "*_test.rego" | while read f; do
    REL_PATH="${f#$POLICY_DIR/}"
    POLICY_ID=$(echo "$REL_PATH" | sed 's|/|.|g;s|\.rego||')

    HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" \
        -X PUT "$OPA_URL/v1/policies/$POLICY_ID" \
        -H "Content-Type: text/plain" \
        --data-binary "@$f")

    if [ "$HTTP_CODE" = "200" ]; then
        echo "  ✓ $POLICY_ID"
    else
        echo "  ✗ $POLICY_ID (HTTP $HTTP_CODE)"
    fi
done

echo ""
echo "Active policies:"
curl -s "$OPA_URL/v1/policies" | python3 -c "
import sys, json
data = json.load(sys.stdin)
for p in data.get('result', []):
    print(f\"  - {p.get('id', 'unknown')}\")
" 2>/dev/null || echo "  (requires python3 to list)"
