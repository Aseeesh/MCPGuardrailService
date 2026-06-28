"""
Real-time policy monitoring — tracks effectiveness, false positive rates, and version status.
Used by the monitoring dashboard and CI/CD alerting.
"""

import json
from datetime import datetime, timezone, timedelta
from dataclasses import dataclass, field


@dataclass
class PolicyMetrics:
    policy_id: str
    version: str
    total_evaluations: int = 0
    true_positives: int = 0
    false_positives: int = 0
    true_negatives: int = 0
    false_negatives: int = 0
    avg_latency_ms: float = 0
    p99_latency_ms: float = 0
    last_evaluation: str = ""
    error_count: int = 0
    latencies: list[float] = field(default_factory=list)

    @property
    def precision(self) -> float:
        tp_fp = self.true_positives + self.false_positives
        return self.true_positives / tp_fp if tp_fp > 0 else 0

    @property
    def recall(self) -> float:
        tp_fn = self.true_positives + self.false_negatives
        return self.true_positives / tp_fn if tp_fn > 0 else 0

    @property
    def f1_score(self) -> float:
        p, r = self.precision, self.recall
        return 2 * p * r / (p + r) if (p + r) > 0 else 0

    @property
    def false_positive_rate(self) -> float:
        fp_tn = self.false_positives + self.true_negatives
        return self.false_positives / fp_tn if fp_tn > 0 else 0


class PolicyMonitor:
    def __init__(self):
        self._metrics: dict[str, PolicyMetrics] = {}
        self._alerts: list[dict] = []
        self._thresholds = {
            "max_false_positive_rate": 0.05,
            "max_p99_latency_ms": 100,
            "min_precision": 0.90,
            "max_error_rate": 0.01,
        }

    def record_evaluation(
        self,
        policy_id: str,
        version: str,
        had_violation: bool,
        was_correct: bool | None,
        latency_ms: float,
        error: bool = False,
    ):
        if policy_id not in self._metrics:
            self._metrics[policy_id] = PolicyMetrics(policy_id=policy_id, version=version)

        m = self._metrics[policy_id]
        m.total_evaluations += 1
        m.last_evaluation = datetime.now(timezone.utc).isoformat()
        m.latencies.append(latency_ms)

        # Keep only last 1000 latencies
        if len(m.latencies) > 1000:
            m.latencies = m.latencies[-1000:]

        m.avg_latency_ms = sum(m.latencies) / len(m.latencies)
        sorted_lat = sorted(m.latencies)
        m.p99_latency_ms = sorted_lat[int(len(sorted_lat) * 0.99)] if sorted_lat else 0

        if error:
            m.error_count += 1

        if was_correct is not None:
            if had_violation and was_correct:
                m.true_positives += 1
            elif had_violation and not was_correct:
                m.false_positives += 1
            elif not had_violation and was_correct:
                m.true_negatives += 1
            elif not had_violation and not was_correct:
                m.false_negatives += 1

        self._check_thresholds(m)

    def _check_thresholds(self, m: PolicyMetrics):
        if m.total_evaluations < 50:
            return

        if m.false_positive_rate > self._thresholds["max_false_positive_rate"]:
            self._alert("high_false_positive_rate", m.policy_id,
                f"FP rate {m.false_positive_rate:.3f} exceeds {self._thresholds['max_false_positive_rate']}")

        if m.p99_latency_ms > self._thresholds["max_p99_latency_ms"]:
            self._alert("high_latency", m.policy_id,
                f"p99 latency {m.p99_latency_ms:.1f}ms exceeds {self._thresholds['max_p99_latency_ms']}ms")

        if m.precision < self._thresholds["min_precision"]:
            self._alert("low_precision", m.policy_id,
                f"Precision {m.precision:.3f} below {self._thresholds['min_precision']}")

        error_rate = m.error_count / m.total_evaluations if m.total_evaluations > 0 else 0
        if error_rate > self._thresholds["max_error_rate"]:
            self._alert("high_error_rate", m.policy_id,
                f"Error rate {error_rate:.3f} exceeds {self._thresholds['max_error_rate']}")

    def _alert(self, alert_type: str, policy_id: str, message: str):
        recent = [a for a in self._alerts
                  if a["type"] == alert_type and a["policy_id"] == policy_id
                  and (datetime.now(timezone.utc) - datetime.fromisoformat(a["timestamp"])) < timedelta(minutes=5)]
        if recent:
            return

        self._alerts.append({
            "type": alert_type,
            "policy_id": policy_id,
            "message": message,
            "severity": "critical" if "error" in alert_type else "warning",
            "timestamp": datetime.now(timezone.utc).isoformat(),
        })

    def get_dashboard(self) -> dict:
        metrics_list = []
        for m in self._metrics.values():
            metrics_list.append({
                "policy_id": m.policy_id,
                "version": m.version,
                "total_evaluations": m.total_evaluations,
                "precision": round(m.precision, 4),
                "recall": round(m.recall, 4),
                "f1_score": round(m.f1_score, 4),
                "false_positive_rate": round(m.false_positive_rate, 4),
                "avg_latency_ms": round(m.avg_latency_ms, 2),
                "p99_latency_ms": round(m.p99_latency_ms, 2),
                "error_count": m.error_count,
                "last_evaluation": m.last_evaluation,
            })

        active_alerts = [a for a in self._alerts
                         if (datetime.now(timezone.utc) - datetime.fromisoformat(a["timestamp"])) < timedelta(hours=1)]

        return {
            "timestamp": datetime.now(timezone.utc).isoformat(),
            "total_policies": len(self._metrics),
            "total_evaluations": sum(m.total_evaluations for m in self._metrics.values()),
            "policies": metrics_list,
            "active_alerts": active_alerts,
            "thresholds": self._thresholds,
        }

    def get_policy_metrics(self, policy_id: str) -> dict | None:
        m = self._metrics.get(policy_id)
        if not m:
            return None
        return {
            "policy_id": m.policy_id,
            "version": m.version,
            "total_evaluations": m.total_evaluations,
            "confusion_matrix": {
                "true_positives": m.true_positives,
                "false_positives": m.false_positives,
                "true_negatives": m.true_negatives,
                "false_negatives": m.false_negatives,
            },
            "precision": round(m.precision, 4),
            "recall": round(m.recall, 4),
            "f1_score": round(m.f1_score, 4),
            "false_positive_rate": round(m.false_positive_rate, 4),
            "latency": {
                "avg_ms": round(m.avg_latency_ms, 2),
                "p99_ms": round(m.p99_latency_ms, 2),
            },
            "error_count": m.error_count,
        }
