# Cloud Sync + Local API Implementation Plan

**Status:** Draft v3 — iteration 3 (final SQL fixes)
**Mode:** DELIBERATE (high-risk: touches 3-register sync pipeline, external network dependencies, secrets)
**Date:** 2026-04-24
**Tracks:** A (Cloud Sync Phases A-E) + B (ASP.NET Web API, parallel)
**Branch:** All implementation work happens on a new branch `feat/cloud-sync-and-local-api` cut from the current stable `main`. Do NOT work on `main`. Branch is created before Phase A Gate A0.1. PR opened early (draft) to track progress; merged to `main` only after Phase A is proven stable for 1 week AND Track B acceptance criteria pass. Long-running feature branch is acceptable for this scope; rebase onto `main` weekly to pick up any POS fixes.

---

## 0. Branching & Workflow (NEW)

Before any Phase A work begins:
1. `git checkout main && git pull`
2. `git checkout -b feat/cloud-sync-and-local-api`
3. `git push -u origin feat/cloud-sync-and-local-api`
4. Open a draft PR immediately: `gh pr create --draft --title "Cloud Sync + Local API (Phase 6)" --body "Tracks implementation of plans/cloud-sync-and-local-api.md"`
5. CI must stay green on the branch — `dotnet build`, `dotnet test Kasir.Core.Tests` on every push.
6. Commits: one commit per deliverable (per the phase-6 doc's commit-style rule). NO `Co-Authored-By: Claude` lines.
7. Weekly rebase onto `main` while the branch is open.
8. Merge gate: all Phase A-E acceptance criteria green + Track B acceptance criteria green + 1 week stability on Phase A + security-reviewer sign-off.

---

## 1. RALPLAN-DR Summary

### Principles

1. **POS must continue selling with zero internet.** Local SQLite remains source of truth. Cloud sync is additive; cloud failure must never block a sale.
2. **Outbox is the contract.** The SMB outbox pattern is the integration boundary. Cloud sync reads from it; it does not modify the POS hot path.
3. **Free-tier budget.** Supabase free (500MB DB), Cloudflare R2 free tier. No paid services until ceiling is approached with monitoring in place.
4. **Schema-first, mapper-enforced.** SQLite-to-Postgres type mappings are explicit per column. Compilation breaks if a mapper misses a column. Schema drift is caught in CI.
5. **Coordinated multi-register migration.** Any schema change to `sync_queue` must land on all 3 registers before the first post-migration sync cycle. HMAC signatures must remain valid across the transition.

### Decision Drivers (Top 3)

1. **Reliability of existing LAN sync** — any change to `sync_queue` or `SyncQueueEntry` risks breaking the working 3-register sync pipeline. Deploy order and backward compatibility are paramount.
2. **Data integrity in the Postgres mirror** — 343K rows with legacy FoxPro-migrated data containing inconsistent date formats, potential FK orphans, and INTEGER-encoded money that must not lose precision in transit.
3. **Operational simplicity** — one solo developer maintaining this on old hardware. The fewer moving parts (services, configs, rotation schedules), the better.

### Viable Options Considered

#### Option A: Outbox-based cloud sync via `Kasir.CloudSync` worker (CHOSEN)

Gateway reads `sync_queue WHERE cloud_synced = 0`, maps rows, upserts to Supabase Postgres.

| Pros | Cons |
|------|------|
| Reuses existing outbox; no new write path in POS | Requires schema migration on 3 registers |
| Idempotent replay via ON CONFLICT | Minutes of lag (acceptable per requirements) |
| Gateway is the only component with cloud credentials | New C# project to maintain |
| Decoupled from LAN sync — independent bookkeeping | Initial load needs separate bulk path |

#### Option B: Litestream-only (replicate entire SQLite to cloud, query via read replica)

Stream WAL to R2, restore to a cloud VM, query the restored SQLite directly.

| Pros | Cons |
|------|------|
| Zero code: just Litestream config | No Postgres — no `pg_trgm`, no SQL analytics |
| Perfect fidelity (it IS the SQLite file) | Requires a cloud VM to host restored SQLite ($5-10/mo) |
| No mapper maintenance | Can't query in real-time (restore lag) |
| | No selective table sync or transformation |

**Why rejected:** Does not provide a queryable Postgres mirror for dashboards/analytics. Cannot do `pg_trgm` search. Requires paid cloud VM. Still used as a complementary disaster recovery layer (Litestream to R2).

#### Option C: Direct SQLite-to-Postgres replication via pgloader

One-shot pgloader runs on a schedule, replacing Postgres contents each time.

| Pros | Cons |
|------|------|
| No custom C# code for sync | Full table replace is destructive and slow |
| pgloader handles type mapping | No incremental sync; 55MB full load each cycle |
| | pgloader .NET 10 / Windows compatibility is poor |
| | No outbox integration; misses deletes between runs |

**Why rejected:** pgloader has poor Windows support, cannot do incremental sync, and a full 55MB reload every cycle is wasteful. Also rejected for initial load per locked decision #9 (custom C# loader with `NpgsqlBinaryImporter` preferred).

### Pre-mortem (3 Failure Scenarios)

#### Scenario 1: Schema migration breaks LAN sync mid-day

**What happens:** `ALTER TABLE sync_queue ADD COLUMN cloud_synced` is applied to Register 01 but not Register 02/03. Register 02 pushes a batch; Register 01's `PullService` tries to process it. The `MapEntry` method encounters the new columns in `SELECT *` results on Register 01 but they don't exist in incoming data from Register 02.

**Detection:** `PullService` throws on import; sync_log shows errors. Registers diverge.

**Mitigation:** (a) Deploy schema change to ALL 3 registers simultaneously during a planned downtime window (end of business day). (b) The new columns have `DEFAULT 0` / `NULL` — `MapEntry` reads them by name, so a register that hasn't been updated yet won't crash on SELECT because the columns simply won't be in its schema. The risk is the REVERSE: updated Register 01 code expecting the columns when reading data from its own DB before migration. Therefore: **schema first, code second.** Run ALTER TABLE on all 3 registers, THEN deploy the updated binary. (c) Add a pre-flight check in `SyncEngine` startup that verifies expected columns exist.

#### Scenario 2: TLS handshake to Supabase fails on target hardware

**Target OS:** Windows 10 (upgraded from the legacy Win7 SP1 noted in CLAUDE.md). Windows 10 supports TLS 1.2/1.3 natively, so this scenario is materially lower risk than when the target was Win7 — but still worth verifying because Supabase may require SNI + specific cipher suites.

**What happens:** Npgsql cannot negotiate TLS with Supabase. CloudSync service crashes on startup.

**Detection:** Gate A0.1 catches it pre-implementation. If missed, service logs handshake error immediately on start.

**Mitigation:** (a) Gate A0.1 is the primary control — trivial to run on Win10. (b) Ensure Win10 is patched past 1809 (TLS 1.3 enabled by default from 1903+). (c) Fallback: use `SslMode=Prefer` if `Require` negotiation fails. (d) Worst case: run CloudSync on a newer LAN machine connecting to the hub's kasir.db via SMB share.

**Long-term risk (formerly Scenario 2): Supabase free tier 500MB ceiling hit silently.** Mitigated by Phase D health check (`pg_database_size`), 400MB alert threshold, `PostgresSink` caps local retry queue at 100K rows, and Phase E ops runbook monthly size check.

#### Scenario 3: Legacy FoxPro date formats cause silent data corruption in Postgres

**What happens:** Some rows migrated from FoxPro have dates like `20260101` (no separators), `01/26/2026` (US format), or empty strings instead of NULL. The mapper's `DateTime.Parse()` either throws (good) or parses incorrectly to wrong dates (bad — silent corruption).

**Detection:** Data-quality pass in Phase B/C catches known bad patterns. But novel formats may slip through.

**Mitigation:** (a) Phase B data-quality pass scans ALL TEXT columns that map to TIMESTAMPTZ, cataloging distinct date formats. (b) Mapper uses `DateTime.TryParseExact` with an explicit allowlist of formats, falling back to NULL + error log rather than guessing. (c) Row-count parity check in Phase C catches rows dropped by parse failures. (d) Add a `_sync_warnings` table in Postgres to log rows with parse issues for manual review.

#### Scenario 4: Litestream Windows binary does not exist

**What happens:** Phase A Litestream install fails. DR story collapses.

**Detection:** Gate A0.2 catches pre-implementation.

**Mitigation:** Custom `WalBackupService.cs` in Kasir.CloudSync using sqlite3_backup API + AWS SDK to R2 (S3-compatible). Acceptable RPO: 5 minutes (vs Litestream's seconds).

### Expanded Test Plan

#### Unit Tests (per component, in `Kasir.CloudSync.Tests`)

| Component | Test Cases |
|-----------|------------|
| `ProductMapper` | SQLite row with valid data maps correctly; INTEGER money -> BIGINT preserves value; 0/1 -> bool; ISO date -> DateTimeOffset; NULL handling; empty string date -> NULL + warning |
| `SaleMapper` | Sale with line items; register-prefixed doc number preserved as PK; negative amounts (returns) |
| All 57 mappers | Each mapper has at least: happy path, NULL column, edge-case date format |
| `PostgresSink` | UPSERT generates correct SQL; ON CONFLICT updates; batch of 100; error on connection failure triggers retry; idempotent replay (same row twice = same result) |
| `OutboxReader` | Reads `cloud_synced=0 AND status='synced'` rows ordered by id; respects batch limit; marks `cloud_synced=1` after sink confirms; does NOT mark on sink failure |
| `SyncCursor` | Persists last-synced id; resumes from correct position after restart; handles empty outbox |
| `HealthCheck` | Reports correct lag; error count increments; last-success timestamp |
| `SyncQueueEntry` (updated) | New `CloudSynced` and `CloudSyncedAt` properties serialize/deserialize; backward compat with rows missing these columns |
| `PushService.SignPayload` | HMAC signature is identical before and after sync_queue schema migration for an equivalent batch; `cloud_synced` / `cloud_synced_at` do NOT appear in the signed JSON payload |

#### Integration Tests (require SQLite + Postgres)

| Test | Setup |
|------|-------|
| End-to-end single row | Insert product in SQLite -> outbox entry -> `OutboxReader` picks up -> `ProductMapper` maps -> `PostgresSink` upserts -> verify row in Postgres matches |
| Initial load 1000 rows | Bulk load via `NpgsqlBinaryImporter` -> verify row count parity -> enable FK constraints -> no orphan errors |
| Schema drift detection | Add column to SQLite schema -> run Atlas/migra diff -> verify CI check fails |
| Reconnect after Postgres outage | Sink fails 3 times -> backoff -> reconnect -> catches up -> all rows synced |
| Concurrent LAN sync + cloud sync | LAN sync marks `synced_at`; cloud sync marks `cloud_synced_at`; both independent; no interference |
| Win10 TLS smoke | Target Windows 10 hardware connects to Supabase via Npgsql with `SslMode=Require`; handshake succeeds |

#### E2E Tests (manual, documented in runbook)

| Test | Procedure |
|------|-----------|
| Full sync cycle | Make a sale on Register 02 -> verify it appears in Supabase within 60s |
| Internet outage resilience | Disconnect gateway from internet -> make 50 sales -> reconnect -> verify all 50 appear in Supabase |
| Litestream restore drill | Stop Litestream -> restore from R2 -> verify restored DB matches source row counts |
| Gateway restart | Kill `Kasir.CloudSync` service -> restart -> verify it resumes from last cursor, no duplicates |
| Schema migration drill | Apply ALTER TABLE on test DB -> deploy updated binary -> verify sync continues |

#### Observability

| Signal | Implementation |
|--------|---------------|
| Sync lag per table | `HealthCheck` computes `NOW() - MAX(cloud_synced_at)` per table; exposed via health endpoint |
| Error rate | Rolling 5-minute error count in `HealthCheck`; logged to `sync_log` |
| Supabase DB size | Periodic `SELECT pg_database_size(current_database())` logged to health endpoint |
| R2 bucket size | Cloudflare API call in health check (or manual monthly check per runbook) |
| Outbox depth | `SELECT COUNT(*) FROM sync_queue WHERE cloud_synced = 0` exposed in health endpoint |

---

## 2. Work Breakdown

### Phase A: Skeleton + Products Mirror End-to-End

**Goal:** Prove the full pipeline with one table. SQLite product change visible in Supabase within 60 seconds.

#### Phase A Pre-flight Gates (PASS/FAIL before any C# code)

**Gate A0.1 — TLS to Supabase from Windows 10**

Target OS is Windows 10 (Win10 supports TLS 1.2/1.3 natively; the earlier Win7 concern is obsolete). On target hardware, write a minimal 20-line .NET 10 console app using Npgsql that connects to Supabase Postgres with `SslMode=Require`.

- Acceptance: handshake succeeds, returns `SELECT 1` result.
- Failure paths (expected to be rare on Win10):
  - (a) If handshake fails with `Require`, try `SslMode=Prefer` and capture the chosen cipher/protocol.
  - (b) Verify Win10 build ≥ 1903 (TLS 1.3 default). If older, apply latest Windows Updates.
  - (c) If still fails, escalate — likely a firewall/egress issue, not a client TLS issue.
  - (d) Document findings in `plans/phase-a-preflight-results.md`.
- **This is the FIRST task in Phase A. No other Phase A work proceeds until this passes.**

**Gate A0.2 — Litestream or C# WAL-backup fallback**

Check Litestream GitHub releases for a Windows binary. As of 2025 no official Windows build exists.

- Decision tree:
  - If Windows binary exists: proceed as planned.
  - If cross-compile from Go source is feasible: document build procedure, commit binary to repo.
  - If neither: implement `Kasir.CloudSync/Backup/WalBackupService.cs` using SQLite `sqlite3_backup` API + Cloudflare R2 S3-compatible upload via `AWSSDK.S3`. Runs as a separate BackgroundService on a 5-minute interval. Produces point-in-time copies (coarser than Litestream's WAL-level RPO but adequate for DR).
- Acceptance: working backup to R2 verified by download + `PRAGMA integrity_check`.

**Gate A0.3 — sync_queue CHECK constraint expansion**

Current CHECK constraint (Schema.sql:1196-1207) allows 15 tables. `SyncConfig.SyncedTables` declares 17 (adds `discount_partners`, `credit_cards`). SQLite cannot ALTER a CHECK constraint. Full table recreation required:

```sql
BEGIN TRANSACTION;

CREATE TABLE sync_queue_new (
    id              INTEGER PRIMARY KEY,
    register_id     TEXT    NOT NULL,
    table_name      TEXT    NOT NULL CHECK(table_name IN (
        'products','product_barcodes','sales','purchases',
        'cash_transactions','memorial_journals','orders',
        'stock_transfers','stock_adjustments','members',
        'subsidiaries','departments','discounts',
        'accounts','locations','discount_partners','credit_cards'
    )),
    record_key      TEXT    NOT NULL,
    operation       TEXT    NOT NULL CHECK(operation IN ('I','U','D')),
    payload         TEXT    CHECK(payload IS NULL OR json_valid(payload)),
    created_at      TEXT    NOT NULL DEFAULT (datetime('now')),
    synced_at       TEXT,
    status          TEXT    NOT NULL DEFAULT 'pending'
                           CHECK(status IN ('pending','synced','failed')),
    retry_count     INTEGER NOT NULL DEFAULT 0,
    last_error      TEXT,
    cloud_synced    INTEGER NOT NULL DEFAULT 0,
    cloud_synced_at TEXT
);

INSERT INTO sync_queue_new
    SELECT id, register_id, table_name, record_key, operation,
           payload, created_at, synced_at, status, retry_count,
           last_error, 0, NULL
    FROM sync_queue;

DROP TABLE sync_queue;
ALTER TABLE sync_queue_new RENAME TO sync_queue;

CREATE INDEX idx_sync_queue_drain ON sync_queue(status) WHERE status = 'pending';
CREATE INDEX idx_sync_queue_cloud_drain ON sync_queue(cloud_synced, id) WHERE cloud_synced = 0;

COMMIT;
```

- Replaces the simple `ALTER TABLE ... ADD COLUMN` approach in Section 5 (see updated Section 5 below).
- Must run on all 4 DBs (3 registers + hub) in same downtime window.
- Pre-condition: all pending sync_queue rows must be drained before recreation. Verify with: `SELECT COUNT(*) FROM sync_queue WHERE status IN ('pending','failed');` — must be 0. If `status = 'failed'` rows exist, the operator must decide to delete them or force-retry before migration. Document the disposition in `plans/phase-a-preflight-results.md`.
- Acceptance: post-migration, insert a row for each of the 17 tables into a test sync_queue to confirm CHECK passes.

#### Deliverables

| Item | Path / Action |
|------|---------------|
| New C# project | `kasir-pos/Kasir.CloudSync/Kasir.CloudSync.csproj` targeting `net10.0`, `LangVersion=12`, `Nullable=disable` (match Kasir.Core), `TreatWarningsAsErrors=true` |
| Test project | `kasir-pos/Kasir.CloudSync.Tests/Kasir.CloudSync.Tests.csproj` with NUnit + Moq + FluentAssertions (match existing test conventions) |
| Solution update | Add both projects to `Kasir.Avalonia.slnx` |
| NuGet packages | `Npgsql` (verify latest stable for .NET 10 on nuget.org before locking), `Microsoft.Extensions.Hosting`, `Microsoft.Data.Sqlite` (via ProjectReference to Kasir.Core) |
| `Program.cs` | `IHost` setup with `BackgroundService`, config from `appsettings.json` + environment variable override for connection string |
| `CloudSyncConfig.cs` | Supabase connection string, poll interval (30s default), batch size (100), R2 bucket config |
| `OutboxReader.cs` | Polls `sync_queue WHERE cloud_synced = 0 AND status = 'synced' ORDER BY id LIMIT @batch`, delegates to mapper + sink. The `AND status = 'synced'` clause prevents the cloud mirror from seeing rows that have not yet completed LAN sync — this preserves the "LAN sync is authoritative" principle. |
| `Mappers/ProductMapper.cs` | SQLite `products` row -> Postgres UPSERT DTO. All 20+ columns explicitly mapped. |
| `PostgresSink.cs` | `INSERT ... ON CONFLICT (product_code) DO UPDATE` via Npgsql parameterized command. `SslMode=Require`. |
| `SyncCursor.cs` | Tracks last-synced outbox ID in a local `cloud_sync_state` table (new table in SQLite, NOT in sync_queue) |
| Supabase setup | Provision project (Singapore region). Create `products` table with Postgres types. Service-role key. |
| Postgres `products` DDL | `kasir-pos/Kasir.CloudSync/Sql/products.sql` — CREATE TABLE with BIGINT money, BOOLEAN active, TIMESTAMPTZ dates |
| Health stub | `HealthCheck.cs` — logs last sync time, error count to console (full endpoint in Phase D) |
| Litestream setup (parallel) | Install Litestream binary on gateway. Config YAML pointing at `kasir.db` -> Cloudflare R2. Runs as separate Windows service. |

#### Acceptance Criteria

- [ ] `dotnet build Kasir.CloudSync` compiles clean with zero warnings
- [ ] `dotnet test Kasir.CloudSync.Tests` passes with ProductMapper unit tests (valid row, NULL handling, date formats, money precision)
- [ ] End-to-end integration test: INSERT a product in local SQLite -> within 60s, `SELECT` from Supabase Postgres returns matching row with correct types (BIGINT money, BOOLEAN, TIMESTAMPTZ)
- [ ] UPDATE the same product -> Postgres row updates (ON CONFLICT works)
- [ ] DELETE via outbox entry -> Postgres row is soft-deleted or removed (decide: hard delete or soft delete flag)
- [ ] Litestream streams WAL to R2; `litestream restore` produces a valid SQLite file
- [ ] Gateway service starts, runs, and stops cleanly as a Windows console app (service wrapper deferred)
- [ ] Resource budget benchmark: measure POS transaction latency on Register 01 — baseline (POS + LAN sync only) vs loaded (POS + LAN sync + CloudSync + Litestream/WalBackupService). Measure p99 over 100 transactions. PASS if p99 increases ≤50ms. FAIL triggers dedicated mini-PC procurement before Track B begins. Benchmark script: `kasir-pos/Kasir.CloudSync.Tests/Benchmarks/TransactionLatencyBench.cs`.

#### Risks

- Npgsql version incompatibility with .NET 10 preview — mitigate: verify on nuget.org before adding
- Supabase free-tier connection limits (2 concurrent) — mitigate: use single persistent connection in `PostgresSink`
- Litestream Windows binary availability — mitigate: Gate A0.2 resolves this before any code; fallback is custom `WalBackupService.cs` using sqlite3_backup API + R2 upload (5-minute RPO)

#### Rollback

- Remove `Kasir.CloudSync` project from solution. No changes to `Kasir.Core` in this phase. Supabase project can be paused/deleted. Litestream can be uninstalled.

---

### Phase B: Core Table Mappers + Schema Diff CI + Data Quality

**Goal:** Expand to the 6 most important tables. Establish schema drift guardrail.

#### Deliverables

| Item | Path / Action |
|------|---------------|
| `Mappers/SaleMapper.cs` | Maps `sales` (30+ columns) + `sale_items` (child rows bundled). Register-prefixed `id` as TEXT PK. |
| `Mappers/SaleItemMapper.cs` | Maps `sale_items`. FK to `sales.id`. |
| `Mappers/StockMovementMapper.cs` | Maps `stock_movements`. |
| `Mappers/VendorMapper.cs` | Maps `subsidiaries` -> Postgres `vendors` (or keep name `subsidiaries`). |
| `Mappers/DepartmentMapper.cs` | Maps `departments`. |
| Postgres DDL for each table | `kasir-pos/Kasir.CloudSync/Sql/{table}.sql` |
| Schema diff CI | GitHub Actions step: compare SQLite Schema.sql against Postgres DDL using `migra` (Python, pip install). Fail if drift detected. Add to `build.yml`. |
| Data-quality scanner | `kasir-pos/Kasir.CloudSync/DataQuality/QualityScanner.cs` — scans TEXT date columns for non-ISO formats, NULL anomalies, FK orphans. Outputs report to console. |
| `_sync_warnings` Postgres table | Logs rows with parse issues (table_name, record_key, column, raw_value, warning). |
| Mapper source generator | `kasir-pos/Kasir.CloudSync.Generators/MapperGenerator.cs` — Roslyn source generator (or T4 template) that reads a per-table config (column list + type mapping) and emits mapper stubs at compile time. Generating a new mapper requires only adding a config entry; no manual mapper .cs file. |

#### Acceptance Criteria

- [ ] All 6 mappers have unit tests covering: happy path, NULL columns, edge-case dates, money precision
- [ ] Integration test: sale with 3 line items syncs correctly; Postgres FK from `sale_items` to `sales` holds
- [ ] `migra` CI check runs in GitHub Actions; adding a column to Schema.sql without updating Postgres DDL causes CI failure
- [ ] Data-quality scanner runs against `kasir.db` (55MB production copy); report identifies all non-ISO date values and FK orphans
- [ ] `_sync_warnings` table captures rows that failed parsing without stopping the sync
- [ ] Mapper source generator: adding a config entry for a new table produces a compilable mapper stub without writing a manual .cs file

#### Risks

- `migra` requires a live Postgres connection for diffing — mitigate: use a disposable Docker Postgres in CI, or switch to file-based Atlas if Docker is unavailable in CI
- Legacy date formats more varied than expected — mitigate: data-quality scanner runs BEFORE mappers are finalized; add format patterns to an allowlist

#### Rollback

- Revert mapper files. Postgres tables can be dropped. CI step can be removed from `build.yml`.

---

### Phase C: All 57 Tables + Initial Load + FK Constraints

**Goal:** Complete mirror. Every SQLite table has a Postgres equivalent. Initial bulk load verified.

#### Deliverables

| Item | Path / Action |
|------|---------------|
| Remaining ~50 mapper configs | Config entries for remaining ~50 tables fed to the Phase B source generator. Tables with no ongoing sync (e.g., `config`, `counters`) get initial-load-only config flags. |
| `InitialLoader.cs` | `--initial-load` CLI mode. Uses `NpgsqlBinaryImporter` for bulk COPY. FK constraints disabled via `SET session_replication_role = replica`. Batches of 1000-5000 rows per table. |
| FK constraint DDL | `kasir-pos/Kasir.CloudSync/Sql/constraints.sql` — all FK constraints, applied AFTER initial load. |
| Row-count parity verifier | `kasir-pos/Kasir.CloudSync/DataQuality/ParityChecker.cs` — compares `SELECT COUNT(*) FROM {table}` between SQLite and Postgres for all 57 tables. |
| Skip list | Tables to skip in ongoing sync (FTS5 virtual tables, `sync_queue` itself, `sync_log`, `audit_log`). Document which tables are initial-load-only vs ongoing-sync. |

#### Acceptance Criteria

- [ ] `dotnet run --project Kasir.CloudSync -- --initial-load` completes against a copy of production `kasir.db` (343K rows, 55MB)
- [ ] `ParityChecker` reports 0 row-count mismatches across all 57 tables (excluding skip list)
- [ ] FK constraints enabled in Postgres without errors (orphans fixed in data-quality pass or logged to `_sync_warnings`)
- [ ] All mapper unit tests pass (57 x min 3 test cases = 171+ new tests)
- [ ] Ongoing sync continues to work for the 17 outbox-tracked tables after initial load

#### Risks

- 57 mapper configs require careful column/type review — mitigate: source generator from Phase B handles boilerplate; focus review on type mapping correctness per table
- FK orphans in legacy data block constraint enablement — mitigate: data-quality pass in Phase B identifies orphans; fix or exclude before Phase C
- Initial load duration on old hardware — mitigate: test on production-size data; expect 5-15 minutes for 343K rows

#### Rollback

- Drop all Postgres tables and re-run initial load. No changes to Kasir.Core.

---

### Phase D: Observability + Litestream Restore Drill

**Goal:** Production-grade monitoring. Verify disaster recovery actually works.

#### Deliverables

| Item | Path / Action |
|------|---------------|
| `HealthCheck.cs` (full) | HTTP endpoint via `System.Net.HttpListener` on `http://localhost:5080/` returning JSON: `{ tables: { products: { last_sync, lag_seconds, error_count, row_count }, ... }, supabase_db_size_mb, outbox_depth, litestream_last_wal, uptime }`. Uses HttpListener (not Kestrel) to avoid pulling Microsoft.AspNetCore.App into Kasir.CloudSync; keeps deployment slim. Kestrel remains in Track B where it is justified. |
| Alert logic | In `BackgroundService` loop: if any table lag > 15 minutes, log CRITICAL. If Supabase DB > 400MB, log WARNING. If outbox depth > 100K, log CRITICAL and pause cloud sync. |
| Remote health publish | Publish a minimal health summary row to Supabase (`_sync_health` table) every 60 seconds via single-row UPSERT. Enables owner to see sync lag/status from Singapore or anywhere, not just from Register 01 localhost. Minimal payload: `{ register_id, status, lag_seconds, outbox_depth, db_size_mb, updated_at }`. |
| `sync_log` integration | Cloud sync events (success, failure, retry) written to existing `sync_log` table in SQLite for local audit trail. |
| Windows service wrapper | `Kasir.CloudSync` installable as a Windows service via `sc.exe create` or `Microsoft.Extensions.Hosting.WindowsServices`. |
| Litestream restore drill | Documented procedure + script: stop Litestream, `litestream restore -o /tmp/restored.db`, compare row counts against source, verify integrity with `PRAGMA integrity_check`. |
| Ops runbook | `kasir-pos/Kasir.CloudSync/docs/RUNBOOK.md` — startup, shutdown, restart, log locations, alert responses, restore procedure, key rotation, capacity check. |

#### Acceptance Criteria

- [ ] Health endpoint returns valid JSON with all fields populated
- [ ] Simulated lag > 15 min triggers CRITICAL log entry
- [ ] Simulated Supabase DB > 400MB triggers WARNING log entry
- [ ] Outbox depth > 100K pauses cloud sync (does not crash)
- [ ] Windows service installs, starts on boot, stops cleanly, restarts after crash
- [ ] Litestream restore drill succeeds: restored DB passes integrity check and row-count parity

#### Risks

- `HttpListener` on Windows 10 may require `netsh http add urlacl` for non-admin service accounts — mitigate: document setup in runbook
- Litestream Windows binary may not support all WAL modes — mitigate: test with SQLite WAL mode specifically

#### Rollback

- Health endpoint and service wrapper are additive. Remove service registration, revert to console app.

---

### Phase E: pg_trgm Search + Capacity Monitoring + Ops Runbook

**Goal:** Enable product search in Postgres. Establish capacity monitoring for free-tier ceiling.

#### Deliverables

| Item | Path / Action |
|------|---------------|
| `pg_trgm` extension | `CREATE EXTENSION IF NOT EXISTS pg_trgm;` in Supabase SQL editor. |
| Generated `search_text` column | `ALTER TABLE products ADD COLUMN search_text TEXT GENERATED ALWAYS AS (product_code \|\| ' ' \|\| COALESCE(product_name, '') \|\| ' ' \|\| COALESCE(barcode, '')) STORED;` |
| GIN index | `CREATE INDEX idx_products_search_trgm ON products USING gin (search_text gin_trgm_ops);` |
| Search query pattern | Document: `SELECT * FROM products WHERE search_text % @query ORDER BY similarity(search_text, @query) DESC LIMIT 20;` with similarity threshold. |
| Capacity monitoring | Add to health check: Supabase DB size, R2 bucket size (Cloudflare API), projected days until 500MB ceiling based on growth rate. |
| Capacity alert | If projected ceiling < 90 days, log WARNING with recommendation to upgrade or prune. |
| Final ops runbook update | Add: capacity management, search index rebuild, key rotation procedure, monthly checklist. |

#### Acceptance Criteria

- [ ] `SELECT * FROM products WHERE search_text % 'sampo' LIMIT 5` returns relevant products with sub-100ms response
- [ ] Partial SKU search (`KLR-01`) returns matching products
- [ ] Barcode substring search works
- [ ] Health check reports current Supabase DB size in MB
- [ ] Capacity projection calculation is tested (unit test with mock growth data)
- [ ] Ops runbook covers all operational procedures

#### Risks

- `pg_trgm` not available on Supabase free tier — mitigate: verify in Supabase docs (it IS available as of 2025)
- Search performance on 24K products — mitigate: 24K is tiny for Postgres; GIN index handles it trivially

#### Rollback

- Drop the generated column and index. Extension can remain.

---

### Track B (Gated): ASP.NET Web API on Register 01

**Goal:** Self-hosted read-only REST API on the hub for LAN access to POS data.

**Gate:** Track B starts AFTER Phase A pre-flight gates pass AND Phase A end-to-end sync is proven stable for 1 week. Rationale: adding a Kestrel web server to Register 01 during initial cloud sync rollout amplifies risk to Principle 1 (POS must continue selling with zero internet). Track B remains parallel to Phases B-E but gated on Phase A stability.

**Note:** It reads directly from the hub's local SQLite — no Postgres dependency.

#### Deliverables

| Item | Path / Action |
|------|---------------|
| New C# project | `kasir-pos/Kasir.WebApi/Kasir.WebApi.csproj` targeting `net10.0`. Uses ASP.NET Core minimal APIs (NOT OWIN — the phase-6 doc says OWIN for .NET 4.8, but we're on .NET 10 now; use Kestrel). |
| Test project | `kasir-pos/Kasir.WebApi.Tests/Kasir.WebApi.Tests.csproj` |
| NuGet packages | `Microsoft.AspNetCore.App` (implicit in .NET 10 web SDK), `Microsoft.Data.Sqlite` (via Kasir.Core reference) |
| Endpoints (read-only) | `GET /api/products` (paginated), `GET /api/products/{code}`, `GET /api/sales/daily`, `GET /api/sales/range?from=&to=`, `GET /api/stock`, `GET /api/stock/{code}`, `GET /api/reports/trial-balance`, `GET /api/reports/pl`, `GET /api/status` |
| API key auth middleware | `X-API-Key` header validation. Key stored in `appsettings.json` (or env var). |
| HTTPS | Self-signed cert via `dotnet dev-certs` for LAN use. Kestrel HTTPS config. |
| Rate limiting | ASP.NET Core rate limiting middleware: 100 requests/minute per client IP. |
| Windows service | Host as Windows service via `Microsoft.Extensions.Hosting.WindowsServices`. |

#### Acceptance Criteria

- [ ] `GET /api/products?page=1&pageSize=20` returns paginated JSON from hub SQLite
- [ ] `GET /api/products/SAMPO001` returns single product detail
- [ ] `GET /api/sales/daily` returns today's sales summary (total, count, by register)
- [ ] `GET /api/status` returns register connectivity, last sync times
- [ ] Request without `X-API-Key` header returns 401
- [ ] Request with wrong key returns 403
- [ ] 101st request within 1 minute returns 429
- [ ] HTTPS works with self-signed cert on LAN
- [ ] Service starts on boot, accessible from another machine on LAN

#### Risks

- Kestrel on Windows 10 — fully supported by .NET 10; smoke-test HTTPS binding on target hardware during Phase A gate work
- SQLite concurrent access (Web API reads while POS writes) — mitigate: WAL mode already enabled; open read-only connection with `Mode=ReadOnly` in connection string
- Firewall blocking port — mitigate: runbook includes `netsh advfirewall` command for port 5443

#### Rollback

- Remove project from solution. Unregister Windows service. No changes to Kasir.Core.

---

## 3. Schema Drift Handling

### SQLite to Postgres Type Mapping Table

| SQLite Type | Postgres Type | Conversion Notes |
|-------------|---------------|------------------|
| `INTEGER` (money, value x 100) | `BIGINT` | Rupiah cents: 21.5M IDR = 2,150,000,000 cents. Exceeds INT32 max (2.1B). BIGINT required. |
| `INTEGER` (boolean 0/1) | `BOOLEAN` | Mapper: `reader.GetInt32(col) == 1`. Postgres stores as native bool. |
| `INTEGER` (autoincrement rowid) | `BIGINT` | IDs copied verbatim, NOT generated. No SERIAL/IDENTITY. |
| `INTEGER` (quantities) | `INTEGER` | Quantities stay as INT (never exceed 2B). |
| `TEXT` (ISO-8601 datetime) | `TIMESTAMPTZ` | Mapper: `DateTime.TryParseExact` with formats: `yyyy-MM-dd HH:mm:ss`, `yyyy-MM-dd`, `yyyyMMdd`. Fallback: NULL + warning log. All stored as UTC in Postgres. |
| `TEXT` (register-prefixed doc numbers) | `TEXT` | Natural keys preserved. Already globally unique. Used as PK. |
| `TEXT` (general) | `TEXT` | Direct copy. |
| `REAL` (if any) | `DOUBLE PRECISION` | Verify: no REAL columns should exist for money. Flag in data-quality scan. |
| `BLOB` (if any) | `BYTEA` | Unlikely in this schema. Flag if encountered. |
| FTS5 virtual tables | **SKIP** | Not mirrored. Product search via `pg_trgm` instead. |
| `sync_queue` | **SKIP** | Internal to sync mechanism. Not mirrored. |
| `sync_log` | **SKIP** | Local audit only. |
| `audit_log` | **SKIP** | Local audit only. |

### Naming Conventions

- Postgres table and column names: **snake_case** (matching SQLite exactly — no renaming).
- Exception: if `subsidiaries` is confusing, can alias to `vendors` in Postgres with a view. Decision deferred.

### PK Strategy

- Natural keys preserved from SQLite. Register-prefixed document numbers (`KLR-01-2601-0001`) are TEXT PKs.
- Integer PKs (autoincrement) copied as BIGINT. No SERIAL/IDENTITY generation.
- Composite PKs (e.g., `sale_items` with `sale_id` + `line_no`) preserved.

### FK Enforcement

- **Initial load:** FK constraints DEFERRED. Load all tables, then enable constraints.
- **Ongoing sync:** FK constraints IMMEDIATE. Parent rows sync before children (mapper ordering).
- **Orphan handling:** Data-quality pass identifies orphans. Fix in SQLite source if possible; otherwise log to `_sync_warnings` and exclude from FK-constrained tables until fixed.

### Schema Diff CI

- **Tool:** `migra` (Python). Runs in GitHub Actions.
- **How:** Spin up two schemas in a disposable Postgres (Docker): one from `Schema.sql` (converted), one from `Kasir.CloudSync/Sql/*.sql`. `migra` outputs the diff. Non-empty diff = CI failure.
- **Alternative:** Atlas (Go binary). Simpler setup (no Docker needed for declarative diff). Evaluate in Phase B; switch if `migra` is too complex for CI.

---

## 4. Secrets and Security

### Supabase Service-Role Key

| Option | Chosen | Why |
|--------|--------|-----|
| Windows DPAPI (`ProtectedData.Protect`) | **Yes** | Encrypts at rest, tied to Windows user account. Only decryptable on the gateway machine by the service account. |
| Environment variable | Fallback | For development/CI. `SUPABASE_SERVICE_ROLE_KEY` env var. |
| Plain-text `appsettings.json` | **No** | Readable by anyone with file access. |

**Implementation:**
- First run: prompt for key, encrypt with DPAPI, store in `%APPDATA%/KasirCloudSync/credentials.dat`.
- Service startup: decrypt from DPAPI. Fall back to env var if DPAPI file not found (for CI/dev).
- `appsettings.json` contains connection string template with `{SERVICE_ROLE_KEY}` placeholder.

### Cloudflare R2 IAM

- Create a dedicated R2 API token scoped to: `Workers R2 Storage:Edit` on the single bucket only.
- Store credentials in Litestream config file (`/etc/litestream.yml` or `%PROGRAMDATA%/litestream/litestream.yml`).
- Litestream config file permissions: readable only by the service account.

### Npgsql TLS

- Connection string includes `SSL Mode=Require` (Npgsql syntax: `SslMode=Require`).
- Supabase provides TLS by default; no client cert needed.

### Key Rotation Cadence

- **Quarterly:** Supabase service-role key, R2 API token.
- **Procedure:** Generate new key in Supabase/Cloudflare dashboard -> update DPAPI-encrypted credential on gateway -> restart service -> verify sync resumes -> revoke old key.
- **Runbook section** documents exact steps.

### Security Review Gate

- `security-reviewer` agent MUST review before first production deploy.
- Review scope: credential storage, TLS configuration, connection string handling, API key auth (Track B), rate limiting, error messages (no credential leakage in logs).

---

## 5. Outbox Migration Safety

### sync_queue Table Recreation (CHECK constraint expansion)

SQLite cannot ALTER a CHECK constraint. The existing CHECK allows 15 tables but `SyncConfig.SyncedTables` declares 17 (adds `discount_partners`, `credit_cards`). Adding `cloud_synced` / `cloud_synced_at` columns at the same time requires a full table recreation.

**Pre-condition:** All pending sync_queue rows must be resolved before recreation. Verify with: `SELECT COUNT(*) FROM sync_queue WHERE status IN ('pending','failed');` — must be 0. Failed rows require manual resolution (retry or delete) first. If `status = 'failed'` rows exist, the operator must decide to delete them or force-retry before migration. Document the disposition in `plans/phase-a-preflight-results.md`.

```sql
-- Run on ALL 3 registers + hub (4 databases total) in same downtime window
-- Pre-condition: see drain predicate below — must be 0 before proceeding

BEGIN TRANSACTION;

CREATE TABLE sync_queue_new (
    id              INTEGER PRIMARY KEY,
    register_id     TEXT    NOT NULL,
    table_name      TEXT    NOT NULL CHECK(table_name IN (
        'products','product_barcodes','sales','purchases',
        'cash_transactions','memorial_journals','orders',
        'stock_transfers','stock_adjustments','members',
        'subsidiaries','departments','discounts',
        'accounts','locations','discount_partners','credit_cards'
    )),
    record_key      TEXT    NOT NULL,
    operation       TEXT    NOT NULL CHECK(operation IN ('I','U','D')),
    payload         TEXT    CHECK(payload IS NULL OR json_valid(payload)),
    created_at      TEXT    NOT NULL DEFAULT (datetime('now')),
    synced_at       TEXT,
    status          TEXT    NOT NULL DEFAULT 'pending'
                           CHECK(status IN ('pending','synced','failed')),
    retry_count     INTEGER NOT NULL DEFAULT 0,
    last_error      TEXT,
    cloud_synced    INTEGER NOT NULL DEFAULT 0,
    cloud_synced_at TEXT
);

INSERT INTO sync_queue_new
    SELECT id, register_id, table_name, record_key, operation,
           payload, created_at, synced_at, status, retry_count,
           last_error, 0, NULL
    FROM sync_queue;

DROP TABLE sync_queue;
ALTER TABLE sync_queue_new RENAME TO sync_queue;

CREATE INDEX idx_sync_queue_drain ON sync_queue(status) WHERE status = 'pending';
CREATE INDEX idx_sync_queue_cloud_drain ON sync_queue(cloud_synced, id) WHERE cloud_synced = 0;

COMMIT;
```

**Post-migration verification:** Insert a test row for each of the 17 tables to confirm CHECK constraint passes, then delete the test rows.

### Code Changes to SyncQueueEntry Model

File: `kasir-pos/Kasir.Core/Models/SyncQueueEntry.cs`

Add two properties:
- `public int CloudSynced { get; set; }` (default 0)
- `public string CloudSyncedAt { get; set; }` (nullable)

### Code Changes to SyncQueueRepository.cs

File: `kasir-pos/Kasir.Core/Data/Repositories/SyncQueueRepository.cs`

Update `MapEntry` to read the two new columns:
- `CloudSynced = SqlHelper.GetInt(reader, "cloud_synced")`
- `CloudSyncedAt = SqlHelper.GetString(reader, "cloud_synced_at")`

Add new methods for cloud sync:
- `GetPendingCloud(int limit)` — `WHERE cloud_synced = 0 ORDER BY id LIMIT @limit`
- `MarkCloudSynced(int id)` — `UPDATE sync_queue SET cloud_synced = 1, cloud_synced_at = datetime('now','localtime') WHERE id = @id`

### Deploy Order: Schema First, Code Second

**Justification:** The existing `MapEntry` uses `SELECT *` with named column access via `SqlHelper.GetString(reader, "column_name")`. If the schema has new columns but the code doesn't read them, the `SELECT *` returns extra columns that are simply ignored — no crash. But if the code tries to read columns that don't exist in the schema, `SqlHelper.GetString` will throw.

Therefore:
1. **Step 1 (end of business day):** Verify all sync_queue rows are drained (`synced_at IS NOT NULL`). Run the table recreation script (above) on all 4 databases (3 registers + hub). Takes <5 seconds each.
2. **Step 2 (verify):** Confirm columns exist: `PRAGMA table_info(sync_queue)` on each register. Insert a test row for each of the 17 table_name values to verify CHECK constraint; delete test rows.
3. **Step 3:** Deploy updated `Kasir.Core` binary (with new `SyncQueueEntry` properties and `MapEntry` reading them) to all 3 registers + hub.
4. **Step 4:** Verify LAN sync still works: make a sale, trigger sync, check sync_log on hub.
5. **Step 5:** Only THEN start `Kasir.CloudSync` service on the gateway.

### HMAC Alignment Protocol

The HMAC signature in `PushService.SignPayload` signs the JSON batch payload, not individual columns. The `cloud_synced` and `cloud_synced_at` columns are NOT included in the batch payload (they're bookkeeping columns read from `sync_queue` metadata, not from the data tables). Therefore:

- **HMAC signatures are unaffected.** The signed payload contains row data from `FetchRowData` (which does `SELECT *` on the DATA table, not `sync_queue`).
- No HMAC key rotation needed for this migration.
- Verification: unit test that existing HMAC signatures remain valid after schema migration.

---

## 6. Observability and Capacity

### Health Check Endpoint

Exposed at `http://localhost:5080/health` (gateway only, not internet-accessible).

```json
{
  "status": "healthy",
  "uptime_seconds": 86400,
  "tables": {
    "products": {
      "last_sync_utc": "2026-04-24T10:30:00Z",
      "lag_seconds": 45,
      "error_count_5m": 0,
      "sqlite_row_count": 24457,
      "postgres_row_count": 24457
    }
  },
  "outbox_depth": 12,
  "supabase_db_size_mb": 42.5,
  "litestream": {
    "last_wal_utc": "2026-04-24T10:30:15Z",
    "r2_bucket_size_mb": 55.0
  },
  "alerts": []
}
```

### Alert Thresholds

| Condition | Severity | Action |
|-----------|----------|--------|
| Any table lag > 15 minutes | CRITICAL | Log to console + sync_log. Investigate Postgres connectivity. |
| Any table lag > 5 minutes | WARNING | Log to console. Likely transient. |
| Supabase DB size > 400MB (80%) | WARNING | Log + runbook: prune old data or upgrade to Pro tier ($25/mo). |
| Supabase DB size > 475MB (95%) | CRITICAL | Pause non-essential table sync. Alert in health endpoint. |
| Outbox depth > 100,000 rows | CRITICAL | Pause cloud sync to prevent local disk fill. Resume when Postgres recovers. |
| Outbox depth > 10,000 rows | WARNING | Log. Likely internet outage; will catch up. |
| Error rate > 10 errors / 5 minutes | WARNING | Log. Check Postgres connectivity. |
| Litestream WAL age > 1 hour | WARNING | Check Litestream service status. |

### Free-Tier Capacity Monitoring

| Resource | Free Limit | Current Usage | Projected Ceiling |
|----------|-----------|---------------|-------------------|
| Supabase DB | 500MB | ~42MB (57 tables, 343K rows) | Measure: record initial size after Phase A products mirror; Phase D health tracks size over time; Phase E runbook recomputes projection from actual weekly growth rate |
| Supabase bandwidth | 5GB/month | TBD after Phase A | Monitor in Phase D |
| Supabase API requests | 500K/month | ~43K/month (1 req/min x 30 days) | Well within limit |
| Cloudflare R2 storage | 10GB/month | ~55MB (kasir.db) | Well within limit |
| Cloudflare R2 operations | 1M Class A, 10M Class B/month | Low (WAL streams) | Well within limit |

### Escalation Path

1. **Warning threshold hit:** Log entry with specific metric and recommendation.
2. **Critical threshold hit:** Pause affected component. Log entry. Health endpoint shows alert.
3. **Ceiling imminent (<30 days):** Ops runbook: evaluate pruning old sync_queue rows from Postgres, archiving pre-cutover data, or upgrading to Supabase Pro ($25/mo).

---

## 7. Explicit Non-Goals

- **Writable cloud.** Postgres is a read-only mirror. No writes flow back to SQLite. No conflict resolution needed.
- **Multi-store support.** No `store_id` column. Deferred to Phase 6.4 if second store opens.
- **Dashboard UI.** No web frontend in this plan. The Postgres mirror enables future dashboards but building them is out of scope.
- **Real-time cloud writes from POS hot path.** Sales go to local SQLite only. Cloud catches up asynchronously via outbox.
- **Replacing SMB outbox.** The existing LAN sync over SMB is the proven foundation. Cloud sync is additive, not a replacement.
- **Mobile app.** No mobile client. The Web API (Track B) could serve one later, but that's a separate plan.
- **Data migration FROM Postgres back to SQLite.** One-way only.
- **Automated failover.** If the gateway dies, cloud sync pauses. Manual intervention to restart.

---

## 8. Gateway Failure Recovery

**Scenario:** Register 01 (the gateway) suffers a hardware failure.

**Recovery Steps:**

1. Install .NET 10 runtime on replacement machine.
2. Restore kasir.db from latest SMB outbox sync OR Litestream/WalBackup restore from R2.
3. Re-provision Supabase service-role key via DPAPI (tied to new machine account — cannot migrate from dead machine's DPAPI).
4. Restart CloudSync service.
5. Verify sync resumes from last `cloud_synced=0` cursor.

**Credential Recovery:**

- DPAPI is machine-bound. The encrypted credential file from the dead machine is unrecoverable.
- The service-role key must be re-pulled from the Supabase dashboard.
- **Runbook requirement:** keep the service-role key in a password manager outside the gateway, and rotate it after recovery.
- After recovery, rotate the key in Supabase dashboard to invalidate the old (potentially compromised) key from the dead machine.

---

## 9. ADR: Cloud Sync Architecture Decision

### Decision

Implement a C# `Kasir.CloudSync` background worker on the gateway (Register 01) that reads the existing SMB outbox (`sync_queue WHERE cloud_synced = 0`), maps SQLite rows to Postgres types, and upserts to Supabase Postgres (Singapore region). Complement with Litestream streaming WAL to Cloudflare R2 for disaster recovery. Simultaneously ship a self-hosted ASP.NET Core Web API on the hub for LAN read-only access.

### Drivers

1. Owner wants remote visibility into sales/stock without being physically present.
2. Disaster recovery: if the store burns down, data must be recoverable.
3. Free-tier budget: no recurring costs until absolutely necessary.
4. Existing LAN sync must not be disrupted.

### Alternatives Considered

1. **Litestream-only** — rejected: no queryable Postgres, no `pg_trgm` search, requires paid cloud VM.
2. **pgloader scheduled dumps** — rejected: no incremental sync, poor Windows support, full reload is wasteful.
3. **Supabase Realtime / Edge Functions** — rejected: adds complexity, requires internet in the write path (violates offline-first principle).
4. **Firebase / Firestore** — rejected: not Postgres (no SQL analytics), vendor lock-in, no `pg_trgm`.

### Why Chosen

- Reuses the existing outbox pattern (proven, tested, running in production).
- Supabase free tier covers the data volume (42MB of 500MB); actual runway to be measured from real growth rate post-Phase A.
- Litestream provides independent disaster recovery at zero cost.
- `pg_trgm` on Postgres gives superior product search for Indonesian text.
- Single C# codebase (familiar to the developer).
- Clean separation: POS never touches the cloud; gateway is the only internet-connected component.

### Consequences

- **New project to maintain:** `Kasir.CloudSync` adds ~50 mapper files and sync infrastructure. Mapper maintenance cost when Schema.sql changes. Mitigated by compile-time enforcement and CI schema diff.
- **Outbox schema change:** `cloud_synced` columns added to `sync_queue` on all registers. One-time coordinated migration.
- **Operational overhead:** Windows service on gateway needs monitoring. Health check and runbook mitigate.
- **Supabase dependency:** If Supabase shuts down or changes pricing, need to migrate Postgres elsewhere. Mitigated: standard Postgres; can move to any host.

### Follow-ups

- After this plan is stable (3+ months): evaluate building a web dashboard (Phase 6.3) against the Postgres mirror.
- Monitor Supabase free-tier usage; plan upgrade path if ceiling approached.
- If second store opens: add `store_id` column to Postgres schema (Phase 6.4).
- Evaluate whether the Web API (Track B) should also serve from Postgres for richer queries (post-Phase E).
- Hardware assessment: if Register 01 reliability is insufficient as gateway, procure dedicated mini-PC.

---

## Dependency Graph

```
Phase A ──────► Phase B ──────► Phase C ──────► Phase D ──────► Phase E
(pre-flight     (core tables    (all 57 tables  (observability  (pg_trgm +
 gates +         + CI diff +     + initial load)  + Litestream    capacity)
 skeleton +      mapper gen)                      drill)
 products)

              ┌─ Phase A stable for 1 week ─┐
              │                              ▼
              └──────────── Track B (Web API) ──────────────────────────►
                            (gated on Phase A stability proof)
```

---

## Codebase Facts Verified During Planning

| Fact | Finding |
|------|---------|
| Active project location | `kasir-pos/Kasir.Core/` (not `kasir-pos/Kasir/` which is legacy WinForms) |
| SQLite package | `Microsoft.Data.Sqlite` 9.0.4 (not `System.Data.SQLite`) |
| `Nullable` setting | `disable` in Kasir.Core.csproj (CLAUDE.md says `enable` — new project should match actual: `disable`) |
| `SyncQueueEntry` properties | 11 properties. No `cloud_synced` or `cloud_synced_at` yet. |
| `MapEntry` reads by column name | `SqlHelper.GetString(reader, "column_name")` — safe for column additions |
| `SELECT *` in queries | Used in `GetPending` and `GetAfter` — extra columns are ignored by existing code |
| sync_queue CHECK constraint | 15 tables. `SyncConfig.SyncedTables` has 17 (adds `discount_partners`, `credit_cards`). Minor inconsistency to document. |
| Total tables in Schema.sql | 57 `CREATE TABLE` statements confirmed |
| Test framework | NUnit 3.14 + Moq 4.20 + FluentAssertions 7.0 |
| Solution file | `Kasir.Avalonia.slnx` (SDK-style) |
| PushService HMAC | Signs JSON batch payload (data table rows), NOT sync_queue metadata columns |

---

## Revision History

### Iteration 1 (2026-04-24) — Architect + Critic feedback, verdict: ITERATE

1. **Phase A Pre-flight Gates (NEW):** Added 3 mandatory gates before any C# code — TLS to Supabase from Win7 (A0.1), Litestream Windows binary or WalBackupService fallback (A0.2), sync_queue CHECK constraint expansion via table recreation (A0.3).
2. **Pre-mortem rebalanced:** Replaced Scenario 2 (Supabase 500MB ceiling — multi-year problem) with Scenario 2 (TLS handshake failure on Win7 — immediate risk). Original demoted to long-term risk bullet. Added Scenario 4 (Litestream binary missing).
3. **Test plan expanded:** Added `PushService.SignPayload` HMAC stability test (unit) and Win7 TLS smoke test (integration).
4. **Mapper source generator promoted:** Moved from Phase C optional to Phase B required deliverable. Phase C now adds config entries, not manual mapper files.
5. **Register 01 resource budget:** Added p99 latency benchmark acceptance criterion to Phase A with PASS/FAIL threshold (<=50ms increase).
6. **Track B gated:** No longer fully parallel. Starts after Phase A pre-flight gates pass AND 1-week stability proof. Dependency graph updated.
7. **HttpListener for Phase D health:** Replaced Kestrel with `System.Net.HttpListener` to avoid ASP.NET dependency in CloudSync.
8. **Remote health visibility:** Added `_sync_health` table UPSERT to Supabase every 60s in Phase D.
9. **Gateway failure runbook (NEW Section 8):** Recovery steps, DPAPI credential re-provisioning, password manager requirement.
10. **Capacity projection grounded:** Replaced "2-3 years" estimate with measurement-driven approach across Phases A/D/E.
11. **Section 5 rewritten:** Simple ALTER TABLE replaced with full table recreation script including CHECK constraint for all 17 tables, drain pre-condition, and post-migration verification.

### Iteration 3 (2026-04-24) — Final SQL fixes (Architect/Critic converged)

1. **Gate A0.3 + Section 5 SQL corrected:** Replaced incorrect column names (`row_pk`/`op`/`AUTOINCREMENT`) with actual schema names (`record_key`/`operation`/`INTEGER PRIMARY KEY`). Added missing columns (`status`, `retry_count`, `last_error`). CHECK constraint and table structure now match `Schema.sql:1193-1221` exactly. INSERT...SELECT updated to copy all 11 existing columns.
2. **Drain pre-condition predicate fixed:** Replaced `WHERE synced_at IS NULL` with `WHERE status IN ('pending','failed')` to match the actual `status` column semantics. Added operator guidance for resolving `failed` rows before migration.
3. **OutboxReader query filter tightened:** Added `AND status = 'synced'` to the `WHERE cloud_synced = 0` query, ensuring cloud sync only processes rows that have already completed LAN sync — preserving "LAN sync is authoritative" principle.

### Post-consensus update (2026-04-25) — Target OS upgrade to Windows 10

1. **Gate A0.1 retargeted:** Target OS changed from Windows 7 SP1 to Windows 10. TLS 1.2/1.3 is native on Win10 (build ≥ 1903), removing SChannel registry-hack concerns. Gate remains as the first Phase A task but failure paths are simplified.
2. **Pre-mortem Scenario 2 relaxed:** TLS handshake failure is now materially lower risk; noted as retained-but-lower-priority scenario.
3. **Phase D/Track B Win7 caveats removed:** Kestrel and HttpListener are fully supported on Win10; risk notes updated accordingly.
4. **Test plan:** `Win7 TLS smoke` → `Win10 TLS smoke`.
