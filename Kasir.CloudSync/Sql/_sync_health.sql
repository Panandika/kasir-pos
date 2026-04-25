-- Single-row table that the gateway UPSERTs the latest health snapshot to.
-- Owner / accountant can SELECT this from anywhere with Supabase access.
CREATE TABLE IF NOT EXISTS _sync_health (
    id          TEXT        PRIMARY KEY,            -- always 'current' for the live snapshot
    payload     JSONB       NOT NULL,
    updated_at  TIMESTAMPTZ NOT NULL
);
