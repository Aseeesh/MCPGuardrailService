import { useState, useEffect, useCallback } from "react";

interface AuditLog {
  AuditId: string;
  Timestamp: string;
  Action: string;
  Decision: string;
  Confidence: number;
  ViolationCount: number;
  Frameworks: string[] | null;
  HumanReviewed: boolean;
  ResourceType: string;
  PipelineMs: number | null;
  RecordHash: string;
  RemediationAction: string | null;
}

interface Filters {
  action: string;
  decision: string;
  framework: string;
  humanReviewed: string;
}

const card: React.CSSProperties = {
  background: "#1e293b",
  borderRadius: "12px",
  border: "1px solid #334155",
  padding: "20px",
};

const select: React.CSSProperties = {
  background: "#0f172a",
  border: "1px solid #334155",
  borderRadius: "6px",
  color: "#e2e8f0",
  padding: "6px 10px",
  fontSize: "12px",
};

const decisionColors: Record<string, string> = {
  allow: "#22c55e",
  block: "#ef4444",
  redact: "#f97316",
  rewrite: "#eab308",
  escalate: "#a855f7",
};

export default function AuditLogTable() {
  const [logs, setLogs] = useState<AuditLog[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [filters, setFilters] = useState<Filters>({
    action: "",
    decision: "",
    framework: "",
    humanReviewed: "",
  });

  const fetchLogs = useCallback(async () => {
    const params = new URLSearchParams();
    params.set("page", String(page));
    params.set("pageSize", "25");
    if (filters.action) params.set("action", filters.action);
    if (filters.decision) params.set("decision", filters.decision);
    if (filters.framework) params.set("framework", filters.framework);
    if (filters.humanReviewed) params.set("humanReviewed", filters.humanReviewed);

    try {
      const res = await fetch(`/api/audit/logs?${params}`);
      if (res.ok) {
        const data = await res.json();
        setLogs(data.Data);
        setTotal(data.Total);
      }
    } catch {
      // Demo data
      setLogs([
        { AuditId: "a3f71b2c9e4d", Timestamp: new Date().toISOString(), Action: "pipeline_detect", Decision: "allow", Confidence: 0.95, ViolationCount: 0, Frameworks: ["GDPR", "SOC2"], HumanReviewed: false, ResourceType: "text", PipelineMs: 23, RecordHash: "a3f71b2c9e4d8f0a1b2c3d4e5f6a7b8c", RemediationAction: null },
        { AuditId: "b4e82c3d0f5e", Timestamp: new Date(Date.now() - 60000).toISOString(), Action: "remediation_redact", Decision: "redact", Confidence: 0.88, ViolationCount: 2, Frameworks: ["GDPR"], HumanReviewed: false, ResourceType: "email", PipelineMs: 45, RecordHash: "b4e82c3d0f5e9a1b2c3d4e5f6a7b8c9d", RemediationAction: "redact" },
        { AuditId: "c5f93d4e1a6f", Timestamp: new Date(Date.now() - 120000).toISOString(), Action: "remediation_block", Decision: "block", Confidence: 0.97, ViolationCount: 3, Frameworks: ["HIPAA"], HumanReviewed: false, ResourceType: "text", PipelineMs: 12, RecordHash: "c5f93d4e1a6f0b2c3d4e5f6a7b8c9d0e", RemediationAction: "block" },
        { AuditId: "d6a04e5f2b7a", Timestamp: new Date(Date.now() - 300000).toISOString(), Action: "review", Decision: "escalate", Confidence: 0.62, ViolationCount: 4, Frameworks: ["GDPR", "HIPAA"], HumanReviewed: true, ResourceType: "text", PipelineMs: 156, RecordHash: "d6a04e5f2b7a1c3d4e5f6a7b8c9d0e1f", RemediationAction: "escalate" },
        { AuditId: "e7b15f6a3c8b", Timestamp: new Date(Date.now() - 600000).toISOString(), Action: "pipeline_detect", Decision: "allow", Confidence: 0.99, ViolationCount: 0, Frameworks: ["SOC2"], HumanReviewed: false, ResourceType: "code", PipelineMs: 8, RecordHash: "e7b15f6a3c8b2d4e5f6a7b8c9d0e1f2a", RemediationAction: null },
      ]);
      setTotal(5);
    }
  }, [page, filters]);

  useEffect(() => { fetchLogs(); }, [fetchLogs]);

  const updateFilter = (key: keyof Filters, value: string) => {
    setFilters((f) => ({ ...f, [key]: value }));
    setPage(1);
  };

  return (
    <div>
      {/* Filters */}
      <div style={{ ...card, marginBottom: "16px", display: "flex", gap: "12px", alignItems: "center", flexWrap: "wrap" }}>
        <span style={{ fontSize: "13px", color: "#64748b", fontWeight: 600 }}>Filters:</span>
        <select style={select} value={filters.action} onChange={(e) => updateFilter("action", e.target.value)}>
          <option value="">All Actions</option>
          <option value="validate">Validate</option>
          <option value="pipeline_detect">Pipeline Detect</option>
          <option value="remediation_redact">Remediation: Redact</option>
          <option value="remediation_block">Remediation: Block</option>
          <option value="review">Review</option>
        </select>
        <select style={select} value={filters.decision} onChange={(e) => updateFilter("decision", e.target.value)}>
          <option value="">All Decisions</option>
          <option value="allow">Allow</option>
          <option value="block">Block</option>
          <option value="redact">Redact</option>
          <option value="rewrite">Rewrite</option>
          <option value="escalate">Escalate</option>
        </select>
        <select style={select} value={filters.framework} onChange={(e) => updateFilter("framework", e.target.value)}>
          <option value="">All Frameworks</option>
          <option value="GDPR">GDPR</option>
          <option value="HIPAA">HIPAA</option>
          <option value="SOC2">SOC2</option>
        </select>
        <select style={select} value={filters.humanReviewed} onChange={(e) => updateFilter("humanReviewed", e.target.value)}>
          <option value="">All Review Status</option>
          <option value="true">Human Reviewed</option>
          <option value="false">Auto Only</option>
        </select>
        <span style={{ fontSize: "12px", color: "#64748b", marginLeft: "auto" }}>
          {total} records
        </span>
      </div>

      {/* Table */}
      <div style={card}>
        <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "13px" }}>
          <thead>
            <tr style={{ borderBottom: "1px solid #334155", textAlign: "left" }}>
              <th style={{ padding: "8px", color: "#64748b", fontWeight: 600, fontSize: "11px", textTransform: "uppercase" }}>Time</th>
              <th style={{ padding: "8px", color: "#64748b", fontWeight: 600, fontSize: "11px", textTransform: "uppercase" }}>Audit ID</th>
              <th style={{ padding: "8px", color: "#64748b", fontWeight: 600, fontSize: "11px", textTransform: "uppercase" }}>Action</th>
              <th style={{ padding: "8px", color: "#64748b", fontWeight: 600, fontSize: "11px", textTransform: "uppercase" }}>Decision</th>
              <th style={{ padding: "8px", color: "#64748b", fontWeight: 600, fontSize: "11px", textTransform: "uppercase" }}>Confidence</th>
              <th style={{ padding: "8px", color: "#64748b", fontWeight: 600, fontSize: "11px", textTransform: "uppercase" }}>Violations</th>
              <th style={{ padding: "8px", color: "#64748b", fontWeight: 600, fontSize: "11px", textTransform: "uppercase" }}>Frameworks</th>
              <th style={{ padding: "8px", color: "#64748b", fontWeight: 600, fontSize: "11px", textTransform: "uppercase" }}>Pipeline</th>
              <th style={{ padding: "8px", color: "#64748b", fontWeight: 600, fontSize: "11px", textTransform: "uppercase" }}>Review</th>
              <th style={{ padding: "8px", color: "#64748b", fontWeight: 600, fontSize: "11px", textTransform: "uppercase" }}>Hash</th>
            </tr>
          </thead>
          <tbody>
            {logs.map((log) => (
              <tr key={log.AuditId} style={{ borderBottom: "1px solid #1e293b" }}>
                <td style={{ padding: "8px", color: "#94a3b8", fontFamily: "monospace", fontSize: "11px" }}>
                  {new Date(log.Timestamp).toLocaleTimeString()}
                </td>
                <td style={{ padding: "8px", fontFamily: "monospace", fontSize: "11px", color: "#3b82f6" }}>
                  {log.AuditId}
                </td>
                <td style={{ padding: "8px" }}>
                  <span style={{
                    background: "#0f172a",
                    border: "1px solid #334155",
                    borderRadius: "4px",
                    padding: "2px 6px",
                    fontSize: "11px",
                  }}>
                    {log.Action}
                  </span>
                </td>
                <td style={{ padding: "8px" }}>
                  <span style={{
                    color: decisionColors[log.Decision] || "#94a3b8",
                    fontWeight: 600,
                    fontSize: "12px",
                  }}>
                    {log.Decision}
                  </span>
                </td>
                <td style={{ padding: "8px", fontFamily: "monospace" }}>
                  <span style={{
                    color: log.Confidence < 0.7 ? "#ef4444" : log.Confidence < 0.85 ? "#eab308" : "#22c55e",
                  }}>
                    {(log.Confidence * 100).toFixed(0)}%
                  </span>
                </td>
                <td style={{ padding: "8px", textAlign: "center" }}>
                  <span style={{
                    color: log.ViolationCount > 0 ? "#ef4444" : "#22c55e",
                    fontWeight: 600,
                  }}>
                    {log.ViolationCount}
                  </span>
                </td>
                <td style={{ padding: "8px", fontSize: "11px", color: "#94a3b8" }}>
                  {log.Frameworks?.join(", ") || "—"}
                </td>
                <td style={{ padding: "8px", fontFamily: "monospace", fontSize: "11px", color: "#64748b" }}>
                  {log.PipelineMs != null ? `${log.PipelineMs}ms` : "—"}
                </td>
                <td style={{ padding: "8px" }}>
                  {log.HumanReviewed ? (
                    <span style={{ color: "#a855f7", fontSize: "11px", fontWeight: 600 }}>Reviewed</span>
                  ) : (
                    <span style={{ color: "#334155", fontSize: "11px" }}>Auto</span>
                  )}
                </td>
                <td style={{ padding: "8px", fontFamily: "monospace", fontSize: "10px", color: "#475569" }}>
                  {log.RecordHash.substring(0, 12)}...
                </td>
              </tr>
            ))}
          </tbody>
        </table>

        {/* Pagination */}
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginTop: "16px", paddingTop: "12px", borderTop: "1px solid #334155" }}>
          <span style={{ fontSize: "12px", color: "#64748b" }}>
            Page {page} of {Math.ceil(total / 25) || 1}
          </span>
          <div style={{ display: "flex", gap: "8px" }}>
            <button
              style={{
                padding: "6px 12px",
                borderRadius: "6px",
                border: "1px solid #334155",
                background: "transparent",
                color: page > 1 ? "#e2e8f0" : "#334155",
                cursor: page > 1 ? "pointer" : "default",
                fontSize: "12px",
              }}
              onClick={() => page > 1 && setPage(page - 1)}
              disabled={page <= 1}
            >
              Previous
            </button>
            <button
              style={{
                padding: "6px 12px",
                borderRadius: "6px",
                border: "1px solid #334155",
                background: "transparent",
                color: page < Math.ceil(total / 25) ? "#e2e8f0" : "#334155",
                cursor: page < Math.ceil(total / 25) ? "pointer" : "default",
                fontSize: "12px",
              }}
              onClick={() => page < Math.ceil(total / 25) && setPage(page + 1)}
              disabled={page >= Math.ceil(total / 25)}
            >
              Next
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
