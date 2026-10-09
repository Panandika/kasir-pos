#!/usr/bin/env bash
# WP-11b process-level smoke: the real Kasir.CloudSync worker process (hosted
# service, DI graph from Program.cs) against the dashboard's LOCAL Supabase stack.
#
#   1. builds a throw-away hub kasir.db from Kasir.Core/Data/Schema.sql
#      (one product, 50 pcs opening stock)
#   2. on the dashboard: two managers count 30 + 18 = 48, the owner (MFA)
#      presses Terapkan  -> one OPNAME row in public.pos_stock_requests
#   3. starts the worker (dotnet run), waits until applied_at is stamped, stops it
#   4. checks kasir.db: one OPNAME movement, id >= 5e9, on-hand 48
#   5. starts the worker again: still one movement (restart is idempotent)
#
# LOCAL ONLY: refuses anything but 127.0.0.1 / localhost, and refuses to run
# while the stack holds pending requests it did not create (the worker would
# apply them to the throw-away DB and mark them applied).
#
# Usage (from kasir-pos, dashboard stack running):
#   scripts/cross-repo-worker-smoke.sh [supabase-project-id]   (default sinar-makmur-dashboard)
set -euo pipefail

PROJECT="${1:-sinar-makmur-dashboard}"
PG_HOST="${KASIR_SMOKE_PG_HOST:-127.0.0.1}"
PG_PORT="${KASIR_SMOKE_PG_PORT:-54322}"
case "$PG_HOST" in 127.0.0.1|localhost) ;; *) echo "refusing non-local host $PG_HOST" >&2; exit 2 ;; esac
CONN="Host=$PG_HOST;Port=$PG_PORT;Database=postgres;Username=postgres;Password=postgres"
DB_CONTAINER="supabase_db_$PROJECT"
REPO="$(cd "$(dirname "$0")/.." && pwd)"

psqlc() { docker exec -i "$DB_CONTAINER" psql -U postgres -d postgres -v ON_ERROR_STOP=1 -At "$@"; }

RUN="$(uuidgen | tr -d '-' | cut -c1-6)"
CODE="SMK${RUN}A"
VENDOR="SV${RUN}"
OWNER="$(uuidgen | tr 'A-Z' 'a-z')"
MGR1="$(uuidgen | tr 'A-Z' 'a-z')"
MGR2="$(uuidgen | tr 'A-Z' 'a-z')"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/kasir-smoke.XXXXXX")"
KDB="$WORK/kasir.db"
LOG="$WORK/worker.log"
echo "run $RUN  product $CODE  work dir $WORK"

others="$(psqlc -c "select count(*) from public.pos_stock_requests where applied_at is null and target_register in ('hub','ALL')")"
if [ "$others" != "0" ]; then
  echo "refusing: $others pending pos_stock_requests already on the stack (reset it: pnpm db:reset:local)" >&2
  exit 3
fi

# ---- 1. hub kasir.db
sqlite3 "$KDB" < "$REPO/Kasir.Core/Data/Schema.sql" >/dev/null
sqlite3 "$KDB" <<SQL
INSERT OR REPLACE INTO config (key, value) VALUES ('register_id', '01');
INSERT OR REPLACE INTO config (key, value) VALUES ('cost_engine_owns_cost_price', 'true');
INSERT INTO products (product_code, name, dept_code, status, unit, price, buying_price, cost_price, vendor_code,
                      open_price, vat_flag, luxury_tax_flag, is_consignment)
VALUES ('$CODE', 'BARANG SMOKE $RUN', '10', 'A', 'PCS', 500000, 300000, 300000, '$VENDOR', 'N', 'N', 'N', 'N');
INSERT INTO stock_movements (id, product_code, journal_no, movement_type, doc_date, period_code, qty_in, val_in,
                             cost_price, changed_at, created_at)
VALUES (4999000000, '$CODE', 'GSMRY-2609', 'PURCHASE', '2026-09-30', '202609', 5000, 1500000000, 300000,
        '2026-09-30 20:00:00', '2026-09-30 20:00:00');
INSERT OR REPLACE INTO config (key, value) VALUES ('cloud_push_wm_stock_movements', '4999000000');
SQL

# ---- 2. dashboard: count + apply (RPCs under the users' JWT claims)
psqlc >/dev/null <<SQL
insert into auth.users (id, email) values
  ('$OWNER', 'smoke-owner-$RUN@test.local'), ('$MGR1', 'smoke-m1-$RUN@test.local'), ('$MGR2', 'smoke-m2-$RUN@test.local');
insert into public.dashboard_users (email, user_id, role) values
  ('smoke-owner-$RUN@test.local', '$OWNER', 'owner'),
  ('smoke-m1-$RUN@test.local', '$MGR1', 'manager'), ('smoke-m2-$RUN@test.local', '$MGR2', 'manager');
insert into public.subsidiaries (sub_code, name) values ('$VENDOR', 'VENDOR SMOKE') on conflict do nothing;
insert into public.products (product_code, name, dept_code, status, unit, price, buying_price, cost_price, vendor_code)
values ('$CODE', 'BARANG SMOKE $RUN', '10', 'A', 'PCS', 500000, 300000, 300000, '$VENDOR');
SQL
# One open session at a time: reuse one a Playwright run left open, else open one as the owner.
SESSION="$(psqlc -c "select id from public.stock_count_sessions where status = 'open' order by created_at limit 1")"
CREATED_SESSION=""
if [ -z "$SESSION" ]; then
  CREATED_SESSION=1
  SESSION="$(psqlc <<SQL | tail -n 1
select set_config('role', 'authenticated', false);
select set_config('request.jwt.claims', json_build_object('sub', '$OWNER', 'role', 'authenticated', 'aal', 'aal2')::text, false);
select public.create_count_session('Smoke $RUN');
SQL
)"
fi
psqlc >/dev/null <<SQL
select set_config('role', 'authenticated', false);
select set_config('request.jwt.claims', json_build_object('sub', '$MGR1', 'role', 'authenticated', 'aal', 'aal1')::text, false);
select (public.insert_count_entry('$SESSION', '$CODE', 3000, gen_random_uuid(), 'Rak depan')).id;
select set_config('request.jwt.claims', json_build_object('sub', '$MGR2', 'role', 'authenticated', 'aal', 'aal1')::text, false);
select (public.insert_count_entry('$SESSION', '$CODE', 1800, gen_random_uuid(), 'Gudang')).id;
select set_config('request.jwt.claims', json_build_object('sub', '$OWNER', 'role', 'authenticated', 'aal', 'aal2')::text, false);
select public.apply_opname('$SESSION', array['$CODE']);
SQL
echo "dashboard: OPNAME request $(psqlc -c "select id || ' qty=' || qty || ' unit_cost=' || coalesce(unit_cost::text,'NULL') from public.pos_stock_requests where product_code = '$CODE'")"

# ---- 3. run the worker process until the request is applied
dotnet build "$REPO/Kasir.CloudSync" -v q -nologo >/dev/null
WORKER_DLL="$REPO/Kasir.CloudSync/bin/Debug/net10.0/Kasir.CloudSync.dll"
run_worker() {
  dotnet "$WORKER_DLL" \
    --CloudSync:SupabaseConnectionString="$CONN" --CloudSync:KasirDbPath="$KDB" \
    --CloudSync:PollIntervalSeconds=2 >>"$LOG" 2>&1 &
  echo $!
}
PID="$(run_worker)"
applied=""
for _ in $(seq 1 60); do
  applied="$(psqlc -c "select coalesce(applied_by_register, '') from public.pos_stock_requests where product_code = '$CODE'")"
  [ -n "$applied" ] && break
  sleep 1
done
sleep 3 # one more tick: nothing new to do
kill "$PID" 2>/dev/null || true; wait "$PID" 2>/dev/null || true
[ -n "$applied" ] || { echo "FAIL: request not applied within 60 s"; tail -n 40 "$LOG"; exit 1; }
echo "worker: applied_by_register=$applied"

# ---- 4. check the hub
check() {
  local n onhand id
  n="$(sqlite3 "$KDB" "select count(*) from stock_movements where product_code='$CODE' and movement_type='OPNAME'")"
  id="$(sqlite3 "$KDB" "select min(id) from stock_movements where product_code='$CODE' and movement_type='OPNAME'")"
  onhand="$(sqlite3 "$KDB" "select sum(qty_in) - sum(qty_out) from stock_movements where product_code='$CODE'")"
  echo "hub: OPNAME movements=$n id=$id on_hand_x100=$onhand"
  [ "$n" = "1" ] && [ "$id" -ge 5000000000 ] && [ "$onhand" = "4800" ] || { echo "FAIL"; tail -n 40 "$LOG"; exit 1; }
}
check

# ---- 5. restart: idempotent
PID="$(run_worker)"; sleep 8; kill "$PID" 2>/dev/null || true; wait "$PID" 2>/dev/null || true
check
cloud="$(psqlc -c "select count(*) from public.stock_movements where product_code = '$CODE'")"
echo "cloud: stock_movements rows for $CODE = $cloud (the pulled OPNAME is never pushed back)"
[ "$cloud" = "0" ] || { echo "FAIL: pulled movement was pushed back"; exit 1; }
if [ -n "$CREATED_SESSION" ]; then
  psqlc >/dev/null <<SQL
select set_config('role', 'authenticated', false);
select set_config('request.jwt.claims', json_build_object('sub', '$OWNER', 'role', 'authenticated', 'aal', 'aal2')::text, false);
select public.close_session('$SESSION');
SQL
fi
grep -E "Pull:|worker started|Tick failed" "$LOG" | head -n 10
echo "PASS (log: $LOG)"
