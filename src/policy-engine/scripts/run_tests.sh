#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ENGINE_DIR="$(dirname "$SCRIPT_DIR")"

echo "=== Policy Engine Test Suite ==="
echo ""

# Check OPA is installed
if ! command -v opa &> /dev/null; then
    echo "OPA not found. Install: brew install opa (macOS) or download from openpolicyagent.org"
    exit 1
fi

echo "--- Syntax Check ---"
find "$ENGINE_DIR/policies" -name "*.rego" ! -name "*_test.rego" | while read f; do
    if opa check "$f" --strict 2>/dev/null; then
        echo "  ✓ $(basename "$f")"
    else
        echo "  ✗ $(basename "$f")"
        exit 1
    fi
done

echo ""
echo "--- Unit Tests ---"
opa test "$ENGINE_DIR/policies" "$ENGINE_DIR/tests" -v 2>&1 | while read line; do
    if echo "$line" | grep -q "PASS"; then
        echo "  ✓ $line"
    elif echo "$line" | grep -q "FAIL"; then
        echo "  ✗ $line"
    else
        echo "  $line"
    fi
done

echo ""
echo "--- Policy Coverage ---"
opa test "$ENGINE_DIR/policies" "$ENGINE_DIR/tests" --coverage --format json 2>/dev/null | \
    python3 -c "
import sys, json
try:
    data = json.load(sys.stdin)
    cov = data.get('coverage', 0)
    print(f'  Coverage: {cov:.1f}%')
except:
    print('  Coverage: unable to calculate')
" 2>/dev/null || echo "  Coverage: requires python3"

echo ""
echo "=== Done ==="
