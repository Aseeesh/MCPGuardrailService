import { useState } from "react";

type Tab = "dashboard" | "validate" | "compliance" | "audit";

const card: React.CSSProperties = {
  background: "#1e293b",
  borderRadius: "12px",
  border: "1px solid #334155",
  padding: "24px",
};

const statValue: React.CSSProperties = {
  fontSize: "32px",
  fontWeight: 700,
  margin: "8px 0 4px",
};

const statLabel: React.CSSProperties = {
  fontSize: "13px",
  color: "#94a3b8",
};

const btn: React.CSSProperties = {
  padding: "10px 20px",
  borderRadius: "8px",
  border: "none",
  cursor: "pointer",
  fontWeight: 600,
  fontSize: "14px",
  background: "#3b82f6",
  color: "#fff",
};

const textarea: React.CSSProperties = {
  width: "100%",
  minHeight: "120px",
  background: "#0f172a",
  border: "1px solid #334155",
  borderRadius: "8px",
  color: "#e2e8f0",
  padding: "12px",
  fontSize: "14px",
  fontFamily: "monospace",
  resize: "vertical",
};

export default function Dashboard({ activeTab }: { activeTab: Tab }) {
  const [content, setContent] = useState("");
  const [result, setResult] = useState<string | null>(null);

  const handleValidate = async () => {
    try {
      const res = await fetch("/api/guardrail/validate", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          content,
          resourceType: "text",
          frameworks: ["GDPR", "HIPAA", "SOC2"],
        }),
      });
      const data = await res.json();
      setResult(JSON.stringify(data, null, 2));
    } catch {
      setResult("API unavailable — start backend services with docker compose up");
    }
  };

  if (activeTab === "dashboard") {
    return (
      <main style={{ padding: "32px", maxWidth: "1200px", margin: "0 auto" }}>
        <div style={{ display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: "16px", marginBottom: "32px" }}>
          {[
            { label: "Active Policies", value: "12", color: "#3b82f6" },
            { label: "Checks Today", value: "847", color: "#22c55e" },
            { label: "Violations", value: "23", color: "#ef4444" },
            { label: "MCP Tools", value: "5", color: "#a855f7" },
          ].map((s) => (
            <div key={s.label} style={card}>
              <span style={statLabel}>{s.label}</span>
              <div style={{ ...statValue, color: s.color }}>{s.value}</div>
            </div>
          ))}
        </div>

        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "16px" }}>
          <div style={card}>
            <h3 style={{ margin: "0 0 16px", fontSize: "16px" }}>Compliance Frameworks</h3>
            {[
              { name: "GDPR", score: 92, color: "#22c55e" },
              { name: "HIPAA", score: 87, color: "#eab308" },
              { name: "SOC2", score: 95, color: "#22c55e" },
            ].map((f) => (
              <div key={f.name} style={{ marginBottom: "12px" }}>
                <div style={{ display: "flex", justifyContent: "space-between", marginBottom: "4px" }}>
                  <span style={{ fontSize: "14px" }}>{f.name}</span>
                  <span style={{ fontSize: "14px", color: f.color }}>{f.score}%</span>
                </div>
                <div style={{ background: "#0f172a", borderRadius: "4px", height: "8px" }}>
                  <div style={{ background: f.color, borderRadius: "4px", height: "8px", width: `${f.score}%` }} />
                </div>
              </div>
            ))}
          </div>

          <div style={card}>
            <h3 style={{ margin: "0 0 16px", fontSize: "16px" }}>MCP Server Status</h3>
            {[
              { name: "Azure MCP Server", tools: "200+", status: "Connected" },
              { name: "Dawnguard MCP", tools: "4", status: "Connected" },
              { name: "Custom Guardrail MCP", tools: "5", status: "Connected" },
            ].map((s) => (
              <div key={s.name} style={{ display: "flex", justifyContent: "space-between", alignItems: "center", padding: "8px 0", borderBottom: "1px solid #334155" }}>
                <div>
                  <div style={{ fontSize: "14px" }}>{s.name}</div>
                  <div style={{ fontSize: "12px", color: "#64748b" }}>{s.tools} tools</div>
                </div>
                <span style={{ fontSize: "12px", color: "#22c55e", background: "#052e16", padding: "2px 8px", borderRadius: "9999px" }}>{s.status}</span>
              </div>
            ))}
          </div>
        </div>

        <div style={{ ...card, marginTop: "16px" }}>
          <h3 style={{ margin: "0 0 16px", fontSize: "16px" }}>Detection Pipeline</h3>
          <div style={{ display: "flex", alignItems: "center", gap: "12px", fontSize: "14px" }}>
            {["Input", "OPA Policies", "LLM Judge", "Decision", "Remediation"].map((step, i) => (
              <div key={step} style={{ display: "flex", alignItems: "center", gap: "12px" }}>
                <div style={{ background: "#3b82f620", border: "1px solid #3b82f6", borderRadius: "8px", padding: "8px 16px", color: "#93c5fd" }}>
                  {step}
                </div>
                {i < 4 && <span style={{ color: "#475569" }}>→</span>}
              </div>
            ))}
          </div>
        </div>
      </main>
    );
  }

  if (activeTab === "validate") {
    return (
      <main style={{ padding: "32px", maxWidth: "800px", margin: "0 auto" }}>
        <div style={card}>
          <h3 style={{ margin: "0 0 16px", fontSize: "16px" }}>Content Validation</h3>
          <textarea
            style={textarea}
            placeholder="Paste content to validate against GDPR, HIPAA, SOC2..."
            value={content}
            onChange={(e) => setContent(e.target.value)}
          />
          <button style={{ ...btn, marginTop: "12px" }} onClick={handleValidate}>
            Validate Content
          </button>
          {result && (
            <pre style={{ ...textarea, marginTop: "16px", minHeight: "80px", whiteSpace: "pre-wrap" }}>
              {result}
            </pre>
          )}
        </div>
      </main>
    );
  }

  if (activeTab === "compliance") {
    return (
      <main style={{ padding: "32px", maxWidth: "1000px", margin: "0 auto" }}>
        <div style={card}>
          <h3 style={{ margin: "0 0 16px", fontSize: "16px" }}>Compliance Scan Results</h3>
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "14px" }}>
            <thead>
              <tr style={{ borderBottom: "1px solid #334155", textAlign: "left" }}>
                <th style={{ padding: "8px" }}>Framework</th>
                <th style={{ padding: "8px" }}>Control</th>
                <th style={{ padding: "8px" }}>Status</th>
                <th style={{ padding: "8px" }}>Finding</th>
              </tr>
            </thead>
            <tbody>
              {[
                { fw: "GDPR", ctrl: "ART-5", status: "Pass", finding: "Data minimization verified" },
                { fw: "HIPAA", ctrl: "164.312", status: "Fail", finding: "PHI encryption not enforced" },
                { fw: "SOC2", ctrl: "CC6.1", status: "Pass", finding: "Access controls in place" },
                { fw: "HIPAA", ctrl: "164.502", status: "Warning", finding: "Minimum necessary review pending" },
                { fw: "SOC2", ctrl: "CC7.2", status: "Pass", finding: "Audit logging active" },
              ].map((r, i) => (
                <tr key={i} style={{ borderBottom: "1px solid #1e293b" }}>
                  <td style={{ padding: "8px" }}>{r.fw}</td>
                  <td style={{ padding: "8px", fontFamily: "monospace" }}>{r.ctrl}</td>
                  <td style={{ padding: "8px" }}>
                    <span style={{
                      color: r.status === "Pass" ? "#22c55e" : r.status === "Fail" ? "#ef4444" : "#eab308",
                      fontSize: "12px", fontWeight: 600,
                    }}>{r.status}</span>
                  </td>
                  <td style={{ padding: "8px", color: "#94a3b8" }}>{r.finding}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </main>
    );
  }

  return (
    <main style={{ padding: "32px", maxWidth: "1000px", margin: "0 auto" }}>
      <div style={card}>
        <h3 style={{ margin: "0 0 16px", fontSize: "16px" }}>Audit Trail</h3>
        {[
          { time: "14:32:01", action: "validate", resource: "text/email", result: "block", detail: "GDPR violation detected" },
          { time: "14:31:45", action: "remediate", resource: "text/report", result: "redact", detail: "PII redacted from output" },
          { time: "14:30:12", action: "scan", resource: "azure/rg-prod", result: "warning", detail: "2 findings in HIPAA scan" },
          { time: "14:28:33", action: "validate", resource: "text/chat", result: "allow", detail: "No violations found" },
        ].map((e, i) => (
          <div key={i} style={{ display: "flex", gap: "16px", padding: "10px 0", borderBottom: "1px solid #334155", fontSize: "13px", alignItems: "center" }}>
            <span style={{ fontFamily: "monospace", color: "#64748b", minWidth: "70px" }}>{e.time}</span>
            <span style={{ background: "#1e293b", border: "1px solid #334155", borderRadius: "4px", padding: "2px 8px", minWidth: "70px", textAlign: "center" }}>{e.action}</span>
            <span style={{ fontFamily: "monospace", color: "#94a3b8", minWidth: "120px" }}>{e.resource}</span>
            <span style={{
              color: e.result === "allow" ? "#22c55e" : e.result === "block" ? "#ef4444" : "#eab308",
              fontWeight: 600, minWidth: "60px",
            }}>{e.result}</span>
            <span style={{ color: "#64748b" }}>{e.detail}</span>
          </div>
        ))}
      </div>
    </main>
  );
}
