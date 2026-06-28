import { useState } from "react";

interface Violation {
  rule?: string;
  type?: string;
  severity?: string;
  message?: string;
  confidence?: number;
}

interface ReviewItem {
  review_id: string;
  status: string;
  priority: string;
  created_at: string;
  content_preview: string;
  auto_decision: string;
  confidence: number;
  violations: Violation[];
  frameworks: string[];
  escalation_reason: string;
  reviewer: string | null;
  original_content: string;
  ai_output: string | null;
  was_correct: boolean | null;
}

const API = "/api/review";

const card: React.CSSProperties = {
  background: "#1e293b",
  borderRadius: "12px",
  border: "1px solid #334155",
  padding: "20px",
  marginBottom: "16px",
};

const label: React.CSSProperties = {
  fontSize: "11px",
  color: "#64748b",
  textTransform: "uppercase",
  fontWeight: 600,
  marginBottom: "4px",
};

const codeBlock: React.CSSProperties = {
  background: "#0f172a",
  border: "1px solid #334155",
  borderRadius: "8px",
  padding: "12px",
  fontSize: "13px",
  fontFamily: "monospace",
  whiteSpace: "pre-wrap",
  color: "#e2e8f0",
  maxHeight: "200px",
  overflow: "auto",
};

const btn: React.CSSProperties = {
  padding: "10px 20px",
  borderRadius: "8px",
  border: "none",
  cursor: "pointer",
  fontWeight: 600,
  fontSize: "14px",
};

const textarea: React.CSSProperties = {
  width: "100%",
  minHeight: "80px",
  background: "#0f172a",
  border: "1px solid #334155",
  borderRadius: "8px",
  color: "#e2e8f0",
  padding: "10px",
  fontSize: "13px",
  fontFamily: "monospace",
  resize: "vertical",
};

const severityColor: Record<string, string> = {
  critical: "#ef4444",
  high: "#f97316",
  medium: "#eab308",
  low: "#22c55e",
};

export default function ReviewDetail({
  item,
  onComplete,
}: {
  item: ReviewItem;
  onComplete: () => void;
}) {
  const [notes, setNotes] = useState("");
  const [modifiedContent, setModifiedContent] = useState(item.original_content);
  const [reviewer, setReviewer] = useState("admin");
  const [submitting, setSubmitting] = useState(false);
  const [showModify, setShowModify] = useState(false);
  const [feedbackGiven, setFeedbackGiven] = useState(false);

  const handleDecision = async (decision: string) => {
    setSubmitting(true);
    try {
      // Claim first
      await fetch(`${API}/${item.review_id}/claim`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ reviewer }),
      });

      // Then decide
      await fetch(`${API}/${item.review_id}/decide`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          reviewer,
          decision,
          notes,
          modified_content: decision === "modify" ? modifiedContent : undefined,
        }),
      });
      onComplete();
    } catch {
      alert("Failed to submit decision — API may be unavailable");
    }
    setSubmitting(false);
  };

  const handleFeedback = async (wasCorrect: boolean) => {
    try {
      await fetch(`${API}/${item.review_id}/feedback`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          reviewer,
          was_correct: wasCorrect,
          feedback_notes: notes,
        }),
      });
      setFeedbackGiven(true);
    } catch {
      // API unavailable
    }
  };

  return (
    <div>
      {/* Header */}
      <div style={{ ...card, display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <div>
          <div style={{ fontSize: "18px", fontWeight: 700, color: "#f8fafc" }}>
            Review: {item.review_id}
          </div>
          <div style={{ fontSize: "13px", color: "#64748b", marginTop: "4px" }}>
            {item.escalation_reason}
          </div>
        </div>
        <div style={{ display: "flex", gap: "12px", alignItems: "center" }}>
          <span style={{
            fontSize: "12px",
            fontWeight: 600,
            textTransform: "uppercase",
            color: item.priority === "critical" ? "#ef4444" : item.priority === "high" ? "#f97316" : "#eab308",
            background: "#0f172a",
            padding: "4px 12px",
            borderRadius: "9999px",
          }}>
            {item.priority}
          </span>
          <span style={{
            fontSize: "12px",
            fontWeight: 600,
            color: item.confidence < 0.7 ? "#ef4444" : "#eab308",
            background: "#0f172a",
            padding: "4px 12px",
            borderRadius: "9999px",
          }}>
            {(item.confidence * 100).toFixed(0)}% confidence
          </span>
        </div>
      </div>

      {/* Content comparison */}
      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "16px" }}>
        <div style={card}>
          <div style={label}>Original Content</div>
          <div style={codeBlock}>{item.original_content}</div>
        </div>
        <div style={card}>
          <div style={label}>AI Decision: {item.auto_decision.toUpperCase()}</div>
          <div style={codeBlock}>
            {item.ai_output || `Auto-decision: ${item.auto_decision}\nConfidence: ${(item.confidence * 100).toFixed(1)}%\nFrameworks: ${item.frameworks.join(", ")}`}
          </div>
        </div>
      </div>

      {/* Violations */}
      <div style={card}>
        <div style={label}>Violations ({item.violations.length})</div>
        <div style={{ marginTop: "8px" }}>
          {item.violations.length === 0 ? (
            <div style={{ color: "#64748b", fontSize: "13px" }}>No violations detected</div>
          ) : (
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "13px" }}>
              <thead>
                <tr style={{ borderBottom: "1px solid #334155", textAlign: "left" }}>
                  <th style={{ padding: "6px 8px", color: "#64748b", fontWeight: 600 }}>Rule</th>
                  <th style={{ padding: "6px 8px", color: "#64748b", fontWeight: 600 }}>Type</th>
                  <th style={{ padding: "6px 8px", color: "#64748b", fontWeight: 600 }}>Severity</th>
                  <th style={{ padding: "6px 8px", color: "#64748b", fontWeight: 600 }}>Message</th>
                  <th style={{ padding: "6px 8px", color: "#64748b", fontWeight: 600 }}>Confidence</th>
                </tr>
              </thead>
              <tbody>
                {item.violations.map((v, i) => (
                  <tr key={i} style={{ borderBottom: "1px solid #1e293b" }}>
                    <td style={{ padding: "6px 8px", fontFamily: "monospace", color: "#94a3b8" }}>{v.rule || "—"}</td>
                    <td style={{ padding: "6px 8px" }}>{v.type || "—"}</td>
                    <td style={{ padding: "6px 8px" }}>
                      <span style={{ color: severityColor[v.severity || "medium"] || "#94a3b8", fontWeight: 600 }}>
                        {v.severity || "—"}
                      </span>
                    </td>
                    <td style={{ padding: "6px 8px", color: "#cbd5e1", maxWidth: "400px" }}>{v.message || "—"}</td>
                    <td style={{ padding: "6px 8px", fontFamily: "monospace" }}>
                      {v.confidence ? `${(v.confidence * 100).toFixed(0)}%` : "—"}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      </div>

      {/* Reviewer Input */}
      <div style={card}>
        <div style={label}>Reviewer</div>
        <input
          type="text"
          value={reviewer}
          onChange={(e) => setReviewer(e.target.value)}
          style={{
            background: "#0f172a",
            border: "1px solid #334155",
            borderRadius: "6px",
            color: "#e2e8f0",
            padding: "8px 12px",
            fontSize: "13px",
            width: "200px",
            marginBottom: "12px",
          }}
        />

        <div style={{ ...label, marginTop: "8px" }}>Review Notes</div>
        <textarea
          style={textarea}
          placeholder="Add notes about your decision..."
          value={notes}
          onChange={(e) => setNotes(e.target.value)}
        />

        {showModify && (
          <>
            <div style={{ ...label, marginTop: "12px" }}>Modified Content</div>
            <textarea
              style={{ ...textarea, minHeight: "120px" }}
              value={modifiedContent}
              onChange={(e) => setModifiedContent(e.target.value)}
            />
          </>
        )}

        {/* Action buttons */}
        <div style={{ display: "flex", gap: "12px", marginTop: "16px" }}>
          <button
            style={{ ...btn, background: "#22c55e", color: "#052e16" }}
            onClick={() => handleDecision("approve")}
            disabled={submitting}
          >
            Approve (Allow)
          </button>
          <button
            style={{ ...btn, background: "#ef4444", color: "#fff" }}
            onClick={() => handleDecision("reject")}
            disabled={submitting}
          >
            Reject (Block)
          </button>
          <button
            style={{ ...btn, background: "#3b82f6", color: "#fff" }}
            onClick={() => {
              if (showModify) handleDecision("modify");
              else setShowModify(true);
            }}
            disabled={submitting}
          >
            {showModify ? "Submit Modified" : "Modify Content"}
          </button>
        </div>
      </div>

      {/* Feedback */}
      {item.status === "approved" || item.status === "rejected" || item.status === "modified" ? (
        <div style={card}>
          <div style={label}>Was the AI decision correct?</div>
          {feedbackGiven ? (
            <div style={{ color: "#22c55e", fontSize: "14px" }}>Feedback recorded. Thank you!</div>
          ) : (
            <div style={{ display: "flex", gap: "12px", marginTop: "8px" }}>
              <button
                style={{ ...btn, background: "#22c55e20", color: "#22c55e", border: "1px solid #22c55e" }}
                onClick={() => handleFeedback(true)}
              >
                Yes, correct
              </button>
              <button
                style={{ ...btn, background: "#ef444420", color: "#ef4444", border: "1px solid #ef4444" }}
                onClick={() => handleFeedback(false)}
              >
                No, incorrect
              </button>
            </div>
          )}
        </div>
      ) : null}
    </div>
  );
}
