#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
BASE_URL="${BASE_URL:-http://localhost:5000}"
RESULTS_DIR="$SCRIPT_DIR/results/$(date +%Y%m%d_%H%M%S)"

mkdir -p "$RESULTS_DIR"

echo "=== Guardrail Load Test Suite ==="
echo "Target: $BASE_URL"
echo "Results: $RESULTS_DIR"
echo ""

# Check k6 installed
if ! command -v k6 &> /dev/null; then
    echo "k6 not found. Install: brew install k6 (macOS) or https://k6.io/docs/get-started/installation/"
    exit 1
fi

# Check target is up
if ! curl -s -o /dev/null -w "%{http_code}" "$BASE_URL/health" | grep -q "200"; then
    echo "WARNING: $BASE_URL not responding. Start services first."
fi

echo "--- Test 1: Deterministic Checks ---"
k6 run \
    --env BASE_URL="$BASE_URL" \
    --out json="$RESULTS_DIR/deterministic.json" \
    --summary-export="$RESULTS_DIR/deterministic_summary.json" \
    "$SCRIPT_DIR/k6_deterministic.js" 2>&1 | tee "$RESULTS_DIR/deterministic.log"

echo ""
echo "--- Test 2: Stress & Scale ---"
k6 run \
    --env BASE_URL="$BASE_URL" \
    --out json="$RESULTS_DIR/stress.json" \
    --summary-export="$RESULTS_DIR/stress_summary.json" \
    "$SCRIPT_DIR/k6_stress.js" 2>&1 | tee "$RESULTS_DIR/stress.log"

echo ""
echo "--- Generating Report ---"
python3 -c "
import json, sys
from pathlib import Path

results_dir = Path('$RESULTS_DIR')
report = []

for name in ['deterministic', 'stress']:
    summary_file = results_dir / f'{name}_summary.json'
    if summary_file.exists():
        data = json.loads(summary_file.read_text())
        metrics = data.get('metrics', {})
        dur = metrics.get('http_req_duration', {}).get('values', {})
        report.append({
            'test': name,
            'requests': metrics.get('http_reqs', {}).get('values', {}).get('count', 0),
            'p50_ms': dur.get('p(50)', 0),
            'p95_ms': dur.get('p(95)', 0),
            'p99_ms': dur.get('p(99)', 0),
            'error_rate': metrics.get('errors', {}).get('values', {}).get('rate', 0),
        })
        print(f'{name}: p50={dur.get(\"p(50)\", 0):.1f}ms p95={dur.get(\"p(95)\", 0):.1f}ms p99={dur.get(\"p(99)\", 0):.1f}ms')

(results_dir / 'report.json').write_text(json.dumps(report, indent=2))
print(f'\nReport saved to {results_dir}/report.json')
" 2>/dev/null || echo "Report generation requires python3"

echo ""
echo "=== Load Tests Complete ==="
echo "Results: $RESULTS_DIR"
