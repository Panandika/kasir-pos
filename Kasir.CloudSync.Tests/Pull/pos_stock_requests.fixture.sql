-- Test fixture ONLY: the shape of Supabase pos_stock_requests from RALPLAN 4.1
-- (dashboard migration 0058, WP-03). The real table is created by the dashboard
-- migration; Kasir.CloudSync never creates it in production.
-- PullServicePostgresTests use public.pos_stock_requests when the local stack has
-- it (LIKE ... INCLUDING ALL) and fall back to this file otherwise.
CREATE TABLE IF NOT EXISTS pos_stock_requests (
  id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  request_kind    TEXT NOT NULL
                  CHECK (request_kind IN (
                    'OPNAME','PURCHASE','RETURN_OUT',
                    'PRODUCT_STATUS','NEW_PRODUCT','BARCODE_LINK','PRODUCT_PACK',
                    'VENDOR_BILL'
                  )),
  idempotency_key TEXT NOT NULL,
  product_code    TEXT,
  qty             BIGINT,
  unit_cost       BIGINT,
  vendor_code     TEXT,
  doc_no          TEXT,
  target_register TEXT NOT NULL DEFAULT 'ALL',
  payload         JSONB,
  happened_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
  created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
  applied_at      TIMESTAMPTZ,
  applied_by_register TEXT,
  -- dashboard 0072: set by the hub when it rejects a request for good
  failed_at       TIMESTAMPTZ,
  failed_reason   TEXT,
  failed_by_register TEXT,
  UNIQUE (request_kind, idempotency_key)
);
CREATE INDEX IF NOT EXISTS idx_psr_pending
  ON pos_stock_requests(applied_at, target_register) WHERE applied_at IS NULL AND failed_at IS NULL;
