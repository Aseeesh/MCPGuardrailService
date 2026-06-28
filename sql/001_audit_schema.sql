-- Immutable Audit Trail Schema
-- Write-once tables with cryptographic hash chain for tamper detection

-- Tenants
CREATE TABLE IF NOT EXISTS tenants (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name            VARCHAR(255) NOT NULL UNIQUE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Immutable audit log — append-only, no UPDATE/DELETE allowed
CREATE TABLE IF NOT EXISTS audit_log (
    id              BIGSERIAL PRIMARY KEY,
    audit_id        VARCHAR(32) NOT NULL UNIQUE,
    tenant_id       UUID NOT NULL REFERENCES tenants(id),
    timestamp       TIMESTAMPTZ NOT NULL DEFAULT now(),

    -- What happened
    action          VARCHAR(50) NOT NULL,  -- validate, remediate, block, escalate, review, feedback
    resource_type   VARCHAR(50) NOT NULL,  -- text, code, config, email, api_response
    source          VARCHAR(50) NOT NULL,  -- user_input, llm_output, api_request, system

    -- Detection results
    decision        VARCHAR(20) NOT NULL,  -- allow, block, redact, rewrite, escalate
    confidence      DECIMAL(5,4) NOT NULL DEFAULT 0,
    violation_count INTEGER NOT NULL DEFAULT 0,

    -- Policy context
    policy_version  VARCHAR(50),
    frameworks      TEXT[],               -- {GDPR,HIPAA,SOC2}

    -- Timing
    pipeline_ms     INTEGER,              -- total pipeline execution time

    -- Payloads stored separately (Azure Blob references)
    input_hash      VARCHAR(64) NOT NULL,  -- SHA-256 of sanitized input
    input_blob_ref  VARCHAR(512),          -- Azure Blob Storage URI
    output_blob_ref VARCHAR(512),          -- Azure Blob Storage URI for output/remediated

    -- Remediation
    remediation_action VARCHAR(20),        -- redact, rewrite, block, escalate, none
    redaction_count    INTEGER DEFAULT 0,

    -- Human override
    human_reviewed     BOOLEAN NOT NULL DEFAULT FALSE,
    reviewer_id        VARCHAR(100),
    review_decision    VARCHAR(20),
    review_notes       TEXT,
    reviewed_at        TIMESTAMPTZ,

    -- Hash chain for tamper detection
    previous_hash   VARCHAR(64),           -- SHA-256 hash of previous record
    record_hash     VARCHAR(64) NOT NULL,  -- SHA-256(id + previous_hash + all fields)
    signature       TEXT,                  -- Optional: RSA/ECDSA signature of record_hash

    -- Metadata
    metadata        JSONB DEFAULT '{}'::jsonb
);

-- Indexes for query performance
CREATE INDEX idx_audit_tenant_time ON audit_log (tenant_id, timestamp DESC);
CREATE INDEX idx_audit_action ON audit_log (action, timestamp DESC);
CREATE INDEX idx_audit_decision ON audit_log (decision, timestamp DESC);
CREATE INDEX idx_audit_frameworks ON audit_log USING GIN (frameworks);
CREATE INDEX idx_audit_human_reviewed ON audit_log (human_reviewed) WHERE human_reviewed = TRUE;
CREATE INDEX idx_audit_hash ON audit_log (record_hash);
CREATE INDEX idx_audit_input_hash ON audit_log (input_hash);

-- Violation details — linked to audit log
CREATE TABLE IF NOT EXISTS audit_violations (
    id              BIGSERIAL PRIMARY KEY,
    audit_log_id    BIGINT NOT NULL REFERENCES audit_log(id),
    rule_id         VARCHAR(50) NOT NULL,
    violation_type  VARCHAR(50) NOT NULL,
    severity        VARCHAR(20) NOT NULL,  -- critical, high, medium, low
    message         TEXT NOT NULL,
    remediation     VARCHAR(20),
    confidence      DECIMAL(5,4),
    stage           VARCHAR(30),           -- deterministic, opa, llm-judge
    control_id      VARCHAR(50),           -- regulatory control (e.g., GDPR-ART5)
    evidence_hash   VARCHAR(64)            -- hash of the evidence text (not stored directly)
);

CREATE INDEX idx_violations_audit ON audit_violations (audit_log_id);
CREATE INDEX idx_violations_severity ON audit_violations (severity);
CREATE INDEX idx_violations_rule ON audit_violations (rule_id);
CREATE INDEX idx_violations_control ON audit_violations (control_id) WHERE control_id IS NOT NULL;

-- Compliance snapshots — periodic compliance state
CREATE TABLE IF NOT EXISTS compliance_snapshots (
    id              BIGSERIAL PRIMARY KEY,
    tenant_id       UUID NOT NULL REFERENCES tenants(id),
    framework       VARCHAR(20) NOT NULL,
    snapshot_date   DATE NOT NULL,
    total_checks    INTEGER NOT NULL DEFAULT 0,
    passed          INTEGER NOT NULL DEFAULT 0,
    failed          INTEGER NOT NULL DEFAULT 0,
    coverage_pct    DECIMAL(5,2),
    controls_covered TEXT[],
    controls_gap    TEXT[],
    report_blob_ref VARCHAR(512),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE(tenant_id, framework, snapshot_date)
);

CREATE INDEX idx_snapshots_tenant ON compliance_snapshots (tenant_id, snapshot_date DESC);

-- Archive tracking — records moved to cold storage
CREATE TABLE IF NOT EXISTS audit_archive_log (
    id              BIGSERIAL PRIMARY KEY,
    tenant_id       UUID NOT NULL REFERENCES tenants(id),
    archived_from   BIGINT NOT NULL,       -- first audit_log.id in batch
    archived_to     BIGINT NOT NULL,       -- last audit_log.id in batch
    record_count    INTEGER NOT NULL,
    archive_blob_ref VARCHAR(512) NOT NULL, -- Azure Blob cold tier URI
    archive_hash    VARCHAR(64) NOT NULL,   -- SHA-256 of the archive blob
    archived_at     TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Prevent mutations on audit_log
CREATE OR REPLACE FUNCTION prevent_audit_mutation()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'audit_log is immutable: % operations are not allowed', TG_OP;
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER audit_log_immutable_update
    BEFORE UPDATE ON audit_log
    FOR EACH ROW EXECUTE FUNCTION prevent_audit_mutation();

CREATE TRIGGER audit_log_immutable_delete
    BEFORE DELETE ON audit_log
    FOR EACH ROW EXECUTE FUNCTION prevent_audit_mutation();

-- Hash chain verification function
CREATE OR REPLACE FUNCTION verify_hash_chain(p_tenant_id UUID, p_limit INTEGER DEFAULT 1000)
RETURNS TABLE(id BIGINT, is_valid BOOLEAN, expected_previous VARCHAR, actual_previous VARCHAR) AS $$
DECLARE
    rec RECORD;
    prev_hash VARCHAR(64) := NULL;
BEGIN
    FOR rec IN
        SELECT a.id, a.previous_hash, a.record_hash
        FROM audit_log a
        WHERE a.tenant_id = p_tenant_id
        ORDER BY a.id ASC
        LIMIT p_limit
    LOOP
        id := rec.id;
        expected_previous := prev_hash;
        actual_previous := rec.previous_hash;
        is_valid := (prev_hash IS NULL AND rec.previous_hash IS NULL)
                    OR (prev_hash = rec.previous_hash);
        prev_hash := rec.record_hash;
        RETURN NEXT;
    END LOOP;
END;
$$ LANGUAGE plpgsql;
