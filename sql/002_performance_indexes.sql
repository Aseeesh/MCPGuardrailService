-- Performance optimization indexes and partitioning

-- Audit log partitioning by month for query performance
-- (Run on production — creates partitioned table structure)
-- CREATE TABLE audit_log_partitioned (LIKE audit_log INCLUDING ALL)
--   PARTITION BY RANGE (timestamp);
-- CREATE TABLE audit_log_y2024m01 PARTITION OF audit_log_partitioned
--   FOR VALUES FROM ('2024-01-01') TO ('2024-02-01');

-- Composite indexes for common query patterns
CREATE INDEX IF NOT EXISTS idx_audit_tenant_action_time
    ON audit_log (tenant_id, action, timestamp DESC);

CREATE INDEX IF NOT EXISTS idx_audit_decision_confidence
    ON audit_log (decision, confidence DESC)
    WHERE confidence < 0.85;

CREATE INDEX IF NOT EXISTS idx_audit_remediation
    ON audit_log (remediation_action, timestamp DESC)
    WHERE remediation_action IS NOT NULL;

-- Partial index for pending human reviews (small, fast)
CREATE INDEX IF NOT EXISTS idx_audit_pending_review
    ON audit_log (timestamp DESC)
    WHERE human_reviewed = FALSE AND decision = 'escalate';

-- BRIN index for time-series queries on large tables
CREATE INDEX IF NOT EXISTS idx_audit_timestamp_brin
    ON audit_log USING BRIN (timestamp)
    WITH (pages_per_range = 32);

-- Violations: composite for compliance reporting
CREATE INDEX IF NOT EXISTS idx_violations_control_severity
    ON audit_violations (control_id, severity)
    WHERE control_id IS NOT NULL;

-- Compliance snapshots: fast lookups
CREATE INDEX IF NOT EXISTS idx_snapshots_framework_date
    ON compliance_snapshots (framework, snapshot_date DESC);

-- Connection pool settings (applied via PostgreSQL config)
-- max_connections = 200
-- shared_buffers = 256MB
-- effective_cache_size = 768MB
-- work_mem = 4MB
-- maintenance_work_mem = 64MB

-- Async audit log insert function (for queue-based logging)
CREATE OR REPLACE FUNCTION insert_audit_async(
    p_audit_id VARCHAR,
    p_tenant_id UUID,
    p_action VARCHAR,
    p_resource_type VARCHAR,
    p_source VARCHAR,
    p_decision VARCHAR,
    p_confidence DECIMAL,
    p_violation_count INTEGER,
    p_input_hash VARCHAR,
    p_record_hash VARCHAR,
    p_previous_hash VARCHAR DEFAULT NULL,
    p_pipeline_ms INTEGER DEFAULT NULL,
    p_frameworks TEXT[] DEFAULT NULL,
    p_remediation_action VARCHAR DEFAULT NULL
) RETURNS BIGINT AS $$
DECLARE
    new_id BIGINT;
BEGIN
    INSERT INTO audit_log (
        audit_id, tenant_id, action, resource_type, source,
        decision, confidence, violation_count, input_hash,
        record_hash, previous_hash, pipeline_ms, frameworks,
        remediation_action
    ) VALUES (
        p_audit_id, p_tenant_id, p_action, p_resource_type, p_source,
        p_decision, p_confidence, p_violation_count, p_input_hash,
        p_record_hash, p_previous_hash, p_pipeline_ms, p_frameworks,
        p_remediation_action
    ) RETURNING id INTO new_id;

    RETURN new_id;
END;
$$ LANGUAGE plpgsql;
