import { useState, useEffect, useCallback } from "react";
import ReviewDetail from "./ReviewDetail";

interface ReviewItem {
  review_id: string;
  status: string;
  priority: string;
  created_at: string;
  content_preview: string;
  auto_decision: string;
  confidence: number;
  violations: Array<{
    rule?: string;
    type?: string;
    severity?: string;
    message?: string;
    confidence?: number;
  }>;
  frameworks: string[];
  escalation_reason: string;
  reviewer: string | null;
  review_decision: string | null;
  review_notes: string | null;
  reviewed_at: string | null;
  original_content: string;
  ai_output: string | null;
  was_correct: boolean | null;
}

interface QueueStats {
  total: number;
  by_status: Record<string, number>;
  feedback_entries: number;
}

const API = "/api/review";

const s = {
  container: { padding: "32px", maxWidth: "1200px", margin: "0 auto" } as React.CSSProperties,
  card: {
    background: "#1e293b",
    borderRadius: "12px",
    border: "1px solid #334155",
    padding: "20px",
    marginBottom: "16px",
  } as React.CSSProperties,
  header: {
    display: "flex",
    justifyContent: "space-between",
    alignItems: "center",
    marginBottom: "24px",
  } as React.CSSProperties,
  title: { fontSize: "20px", fontWeight: 700, color: "#f8fafc", margin: 0 } as React.CSSProperties,
  statsRow: { display: "flex", gap: "12px" } as React.CSSProperties,
  stat: {
    background: "#0f172a",
    borderRadius: "8px",
    padding: "8px 16px",
    fontSize: "13px",
  } as React.CSSProperties,
  row: {
    display: "flex",
    justifyContent: "space-between",
    alignItems: "center",
    padding: "14px 0",
    borderBottom: "1px solid #334155",
    cursor: "pointer",
  } as React.CSSProperties,
  btn: {
    padding: "6px 14px",
    borderRadius: "6px",
    border: "none",
    cursor: "pointer",
    fontSize: "12px",
    fontWeight: 600,
  } as React.CSSProperties,
  refreshBtn: {
    padding: "8px 16px",
    borderRadius: "6px",
    border: "1px solid #334155",
    background: "transparent",
    color: "#94a3b8",
    cursor: "pointer",
    fontSize: "13px",
  } as React.CSSProperties,
};

const priorityColors: Record<string, string> = {
  critical: "#ef4444",
  high: "#f97316",
  medium: "#eab308",
  low: "#22c55e",
};

const decisionColors: Record<string, string> = {
  block: "#ef4444",
  redact: "#f97316",
  rewrite: "#eab308",
  escalate: "#a855f7",
  allow: "#22c55e",
};

export default function ReviewQueue() {
  const [items, setItems] = useState<ReviewItem[]>([]);
  const [stats, setStats] = useState<QueueStats | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const fetchData = useCallback(async () => {
    setLoading(true);
    try {
      const [itemsRes, statsRes] = await Promise.all([
        fetch(`${API}/pending`),
        fetch(`${API}/stats`),
      ]);
      if (itemsRes.ok) setItems(await itemsRes.json());
      if (statsRes.ok) setStats(await statsRes.json());
    } catch {
      // API unavailable
    }
    setLoading(false);
  }, []);

  useEffect(() => { fetchData(); }, [fetchData]);

  if (selectedId) {
    const item = items.find((i) => i.review_id === selectedId);
    return (
      <div style={s.container}>
        <button
          style={{ ...s.refreshBtn, marginBottom: "16px" }}
          onClick={() => { setSelectedId(null); fetchData(); }}
        >
          ← Back to Queue
        </button>
        {item ? (
          <ReviewDetail item={item} onComplete={() => { setSelectedId(null); fetchData(); }} />
        ) : (
          <div style={s.card}>Loading review...</div>
        )}
      </div>
    );
  }

  return (
    <div style={s.container}>
      <div style={s.header}>
        <h2 style={s.title}>Human Review Queue</h2>
        <div style={{ display: "flex", gap: "8px", alignItems: "center" }}>
          {stats && (
            <div style={s.statsRow}>
              <span style={{ ...s.stat, color: "#f97316" }}>
                {stats.by_status?.pending || 0} pending
              </span>
              <span style={{ ...s.stat, color: "#3b82f6" }}>
                {stats.by_status?.in_review || 0} in review
              </span>
              <span style={{ ...s.stat, color: "#22c55e" }}>
                {(stats.by_status?.approved || 0) + (stats.by_status?.rejected || 0)} completed
              </span>
            </div>
          )}
          <button style={s.refreshBtn} onClick={fetchData} disabled={loading}>
            {loading ? "Loading..." : "Refresh"}
          </button>
        </div>
      </div>

      {items.length === 0 ? (
        <div style={{ ...s.card, textAlign: "center", padding: "48px", color: "#64748b" }}>
          <div style={{ fontSize: "32px", marginBottom: "12px" }}>All clear</div>
          <div>No items pending review. The detection pipeline is handling decisions automatically.</div>
        </div>
      ) : (
        <div>
          <div style={{ ...s.card, padding: "12px 20px", display: "flex", gap: "16px", fontSize: "12px", color: "#64748b", fontWeight: 600, textTransform: "uppercase" as const }}>
            <span style={{ width: "80px" }}>Priority</span>
            <span style={{ flex: 1 }}>Content Preview</span>
            <span style={{ width: "80px" }}>Decision</span>
            <span style={{ width: "80px" }}>Confidence</span>
            <span style={{ width: "100px" }}>Frameworks</span>
            <span style={{ width: "60px" }}>Violations</span>
            <span style={{ width: "120px" }}>Created</span>
          </div>
          {items.map((item) => (
            <div
              key={item.review_id}
              style={{ ...s.card, padding: "0 20px", cursor: "pointer" }}
              onClick={() => setSelectedId(item.review_id)}
            >
              <div style={s.row}>
                <span style={{
                  width: "80px",
                  color: priorityColors[item.priority] || "#94a3b8",
                  fontWeight: 600,
                  fontSize: "12px",
                  textTransform: "uppercase" as const,
                }}>
                  {item.priority}
                </span>
                <span style={{ flex: 1, fontSize: "13px", color: "#e2e8f0", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" as const, maxWidth: "400px" }}>
                  {item.content_preview}
                </span>
                <span style={{
                  width: "80px",
                  color: decisionColors[item.auto_decision] || "#94a3b8",
                  fontWeight: 600,
                  fontSize: "12px",
                }}>
                  {item.auto_decision}
                </span>
                <span style={{ width: "80px", fontSize: "13px" }}>
                  <span style={{ color: item.confidence < 0.7 ? "#ef4444" : item.confidence < 0.85 ? "#eab308" : "#22c55e" }}>
                    {(item.confidence * 100).toFixed(0)}%
                  </span>
                </span>
                <span style={{ width: "100px", fontSize: "11px", color: "#94a3b8" }}>
                  {item.frameworks.join(", ")}
                </span>
                <span style={{ width: "60px", fontSize: "13px", textAlign: "center" as const }}>
                  {item.violations.length}
                </span>
                <span style={{ width: "120px", fontSize: "11px", color: "#64748b" }}>
                  {new Date(item.created_at).toLocaleString()}
                </span>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
