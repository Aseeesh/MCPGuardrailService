"""
Human-in-the-loop review queue with feedback collection and agent learning.
"""

import hashlib
from datetime import datetime, timezone
from enum import Enum
from pydantic import BaseModel


class ReviewStatus(str, Enum):
    PENDING = "pending"
    IN_REVIEW = "in_review"
    APPROVED = "approved"
    REJECTED = "rejected"
    MODIFIED = "modified"


class ReviewPriority(str, Enum):
    CRITICAL = "critical"
    HIGH = "high"
    MEDIUM = "medium"
    LOW = "low"


class ReviewItem(BaseModel):
    review_id: str
    status: ReviewStatus = ReviewStatus.PENDING
    priority: ReviewPriority = ReviewPriority.MEDIUM
    created_at: str
    updated_at: str | None = None

    # Content
    original_content: str
    ai_output: str | None = None
    content_preview: str

    # Detection results
    auto_decision: str
    confidence: float
    violations: list[dict]
    frameworks: list[str] = []

    # Escalation
    escalation_reason: str
    source_pipeline: str = "detection"

    # Review
    reviewer: str | None = None
    review_decision: str | None = None
    review_notes: str | None = None
    modified_content: str | None = None
    reviewed_at: str | None = None

    # Feedback
    feedback_category: str | None = None
    was_correct: bool | None = None


class FeedbackEntry(BaseModel):
    review_id: str
    timestamp: str
    reviewer: str
    original_decision: str
    human_decision: str
    was_correct: bool
    violation_types: list[str]
    confidence: float
    feedback_notes: str = ""


class ReviewQueue:
    def __init__(self):
        self._queue: dict[str, ReviewItem] = {}
        self._feedback_log: list[FeedbackEntry] = []
        self._thresholds: dict[str, float] = {
            "auto_approve": 0.85,
            "escalation": 0.70,
        }

    def submit(
        self,
        content: str,
        decision: str,
        confidence: float,
        violations: list[dict],
        reason: str,
        ai_output: str | None = None,
        frameworks: list[str] | None = None,
    ) -> ReviewItem:
        now = datetime.now(timezone.utc).isoformat()
        review_id = hashlib.sha256(f"{content}{now}".encode()).hexdigest()[:12]

        priority = ReviewPriority.CRITICAL if any(
            v.get("severity") == "critical" for v in violations
        ) else ReviewPriority.HIGH if any(
            v.get("severity") == "high" for v in violations
        ) else ReviewPriority.MEDIUM if confidence < 0.5 else ReviewPriority.LOW

        item = ReviewItem(
            review_id=review_id,
            created_at=now,
            original_content=content,
            ai_output=ai_output,
            content_preview=content[:200] + ("..." if len(content) > 200 else ""),
            auto_decision=decision,
            confidence=confidence,
            violations=violations[:20],
            frameworks=frameworks or [],
            escalation_reason=reason,
        )
        item.priority = priority
        self._queue[review_id] = item
        return item

    def get_pending(self, limit: int = 50) -> list[ReviewItem]:
        priority_order = {
            ReviewPriority.CRITICAL: 0,
            ReviewPriority.HIGH: 1,
            ReviewPriority.MEDIUM: 2,
            ReviewPriority.LOW: 3,
        }
        items = [
            item for item in self._queue.values()
            if item.status in (ReviewStatus.PENDING, ReviewStatus.IN_REVIEW)
        ]
        items.sort(key=lambda x: (priority_order.get(x.priority, 99), x.created_at))
        return items[:limit]

    def get_item(self, review_id: str) -> ReviewItem | None:
        return self._queue.get(review_id)

    def claim(self, review_id: str, reviewer: str) -> ReviewItem | None:
        item = self._queue.get(review_id)
        if not item or item.status not in (ReviewStatus.PENDING, ReviewStatus.IN_REVIEW):
            return None
        item.status = ReviewStatus.IN_REVIEW
        item.reviewer = reviewer
        item.updated_at = datetime.now(timezone.utc).isoformat()
        return item

    def approve(self, review_id: str, reviewer: str, notes: str = "") -> ReviewItem | None:
        return self._complete_review(review_id, reviewer, ReviewStatus.APPROVED, notes)

    def reject(self, review_id: str, reviewer: str, notes: str = "") -> ReviewItem | None:
        return self._complete_review(review_id, reviewer, ReviewStatus.REJECTED, notes)

    def modify(
        self, review_id: str, reviewer: str, modified_content: str, notes: str = ""
    ) -> ReviewItem | None:
        item = self._complete_review(review_id, reviewer, ReviewStatus.MODIFIED, notes)
        if item:
            item.modified_content = modified_content
        return item

    def submit_feedback(
        self,
        review_id: str,
        reviewer: str,
        was_correct: bool,
        feedback_notes: str = "",
    ) -> FeedbackEntry | None:
        item = self._queue.get(review_id)
        if not item:
            return None

        item.was_correct = was_correct
        item.feedback_category = "correct" if was_correct else "incorrect"

        entry = FeedbackEntry(
            review_id=review_id,
            timestamp=datetime.now(timezone.utc).isoformat(),
            reviewer=reviewer,
            original_decision=item.auto_decision,
            human_decision=item.review_decision or item.auto_decision,
            was_correct=was_correct,
            violation_types=[v.get("type", "") for v in item.violations],
            confidence=item.confidence,
            feedback_notes=feedback_notes,
        )
        self._feedback_log.append(entry)

        self._adjust_thresholds()
        return entry

    def get_feedback_stats(self) -> dict:
        if not self._feedback_log:
            return {"total": 0, "accuracy": 0, "thresholds": self._thresholds}

        total = len(self._feedback_log)
        correct = sum(1 for f in self._feedback_log if f.was_correct)
        accuracy = correct / total if total else 0

        # Per-type accuracy
        type_stats: dict[str, dict[str, int]] = {}
        for entry in self._feedback_log:
            for vtype in entry.violation_types:
                if vtype not in type_stats:
                    type_stats[vtype] = {"correct": 0, "total": 0}
                type_stats[vtype]["total"] += 1
                if entry.was_correct:
                    type_stats[vtype]["correct"] += 1

        return {
            "total_reviews": total,
            "accuracy": round(accuracy, 3),
            "correct": correct,
            "incorrect": total - correct,
            "per_type_accuracy": {
                k: round(v["correct"] / v["total"], 3) if v["total"] else 0
                for k, v in type_stats.items()
            },
            "current_thresholds": self._thresholds,
        }

    def get_stats(self) -> dict:
        statuses = {}
        for item in self._queue.values():
            statuses[item.status.value] = statuses.get(item.status.value, 0) + 1
        return {
            "total": len(self._queue),
            "by_status": statuses,
            "feedback_entries": len(self._feedback_log),
        }

    def _complete_review(
        self, review_id: str, reviewer: str, status: ReviewStatus, notes: str
    ) -> ReviewItem | None:
        item = self._queue.get(review_id)
        if not item:
            return None
        now = datetime.now(timezone.utc).isoformat()
        item.status = status
        item.reviewer = reviewer
        item.review_decision = status.value
        item.review_notes = notes
        item.reviewed_at = now
        item.updated_at = now
        return item

    def _adjust_thresholds(self):
        """Adjust auto-approve/escalation thresholds based on feedback patterns."""
        recent = self._feedback_log[-50:]
        if len(recent) < 10:
            return

        false_positives = sum(
            1 for f in recent
            if not f.was_correct and f.original_decision in ("block", "redact")
        )
        false_negatives = sum(
            1 for f in recent
            if not f.was_correct and f.original_decision == "allow"
        )

        fp_rate = false_positives / len(recent)
        fn_rate = false_negatives / len(recent)

        if fp_rate > 0.15:
            self._thresholds["auto_approve"] = min(0.95, self._thresholds["auto_approve"] + 0.02)
        elif fp_rate < 0.05:
            self._thresholds["auto_approve"] = max(0.75, self._thresholds["auto_approve"] - 0.01)

        if fn_rate > 0.10:
            self._thresholds["escalation"] = max(0.50, self._thresholds["escalation"] - 0.02)
        elif fn_rate < 0.03:
            self._thresholds["escalation"] = min(0.80, self._thresholds["escalation"] + 0.01)
