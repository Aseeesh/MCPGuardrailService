import { useState } from "react";
import Dashboard from "./components/Dashboard";
import ReviewQueue from "./components/review/ReviewQueue";
import AuditDashboard from "./components/audit/AuditDashboard";
import ComplianceDashboard from "./components/compliance/ComplianceDashboard";

const styles: Record<string, React.CSSProperties> = {
  app: {
    fontFamily: "'Segoe UI', system-ui, -apple-system, sans-serif",
    minHeight: "100vh",
    background: "#0f172a",
    color: "#e2e8f0",
  },
  header: {
    background: "linear-gradient(135deg, #1e293b 0%, #0f172a 100%)",
    borderBottom: "1px solid #334155",
    padding: "16px 32px",
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
  },
  logo: {
    display: "flex",
    alignItems: "center",
    gap: "12px",
  },
  title: {
    fontSize: "20px",
    fontWeight: 700,
    color: "#f8fafc",
    margin: 0,
  },
  badge: {
    fontSize: "11px",
    background: "#22c55e",
    color: "#052e16",
    padding: "2px 8px",
    borderRadius: "9999px",
    fontWeight: 600,
  },
  nav: {
    display: "flex",
    gap: "4px",
  },
};

type Tab = "dashboard" | "validate" | "compliance" | "review" | "audit";

export default function App() {
  const [activeTab, setActiveTab] = useState<Tab>("dashboard");

  const tabStyle = (tab: Tab): React.CSSProperties => ({
    padding: "8px 16px",
    borderRadius: "6px",
    border: "none",
    cursor: "pointer",
    fontSize: "13px",
    fontWeight: 500,
    background: activeTab === tab ? "#3b82f6" : "transparent",
    color: activeTab === tab ? "#fff" : "#94a3b8",
  });

  return (
    <div style={styles.app}>
      <header style={styles.header}>
        <div style={styles.logo}>
          <h1 style={styles.title}>Guardrail & Compliance Service</h1>
          <span style={styles.badge}>MCP-Enabled</span>
        </div>
        <nav style={styles.nav}>
          {(["dashboard", "validate", "compliance", "review", "audit"] as Tab[]).map(
            (tab) => (
              <button
                key={tab}
                style={tabStyle(tab)}
                onClick={() => setActiveTab(tab)}
              >
                {tab.charAt(0).toUpperCase() + tab.slice(1)}
              </button>
            )
          )}
        </nav>
      </header>
      {activeTab === "review" ? <ReviewQueue /> : activeTab === "audit" ? <AuditDashboard /> : activeTab === "compliance" ? <ComplianceDashboard /> : <Dashboard activeTab={activeTab} />}
    </div>
  );
}
