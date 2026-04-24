CREATE TABLE IF NOT EXISTS locations (
    location_code  TEXT        PRIMARY KEY,
    name           TEXT        NOT NULL,
    remark         TEXT,
    changed_by     INTEGER,
    changed_at     TIMESTAMPTZ
);
