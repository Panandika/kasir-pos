CREATE TABLE IF NOT EXISTS departments (
    dept_code   TEXT        PRIMARY KEY,
    name        TEXT        NOT NULL,
    changed_by  INTEGER,
    changed_at  TIMESTAMPTZ
);
