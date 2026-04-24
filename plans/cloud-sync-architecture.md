# Cloud Sync Architecture — Offline-First POS with Postgres Mirror + S3 Backup

**Status:** Planning
**Depends on:** Existing LAN sync (outbox pattern over SMB) working in production
**Relates to:** `phase-6-online-features.md` (this is the concrete architecture for Phase 6.2)

---

## Objective

Keep the POS **offline-first** (registers must sell with zero internet) while adding two independent cloud layers:

1. **Supabase Postgres mirror** — queryable, for dashboards and remote reporting
2. **Litestream → S3** — raw SQLite file stream, for disaster recovery

Registers remain dumb. Internet loss must not affect selling. Cloud loss must not affect LAN sync.

---

## Non-Goals

- Real-time cloud writes from the POS hot path
- Making Postgres the source of truth
- Replacing the existing SMB outbox sync between registers
- Multi-store (deferred to Phase 6.4)

---

## Architecture

```
Register 1 (kasir.db) ──┐
Register 2 (kasir.db) ──┼─► SMB outbox ◄─┐
Register 3 (kasir.db) ──┘                 │
                                          │
                                     ┌────┴─────┐
                                     │ Gateway  │
                                     │ (role)   │
                                     └────┬─────┘
                                          │
                        ┌─────────────────┴──────────────────┐
                        │                                    │
                   Supabase Postgres                 Litestream → S3
                   (queryable mirror)                (raw WAL stream)
```

### Three data flows

| Flow | Path | Latency | Offline-tolerant |
|------|------|---------|------------------|
| Sale | Form → Service → SQLite + outbox | ~10ms | Yes (local only) |
| LAN sync | Push/Pull via SMB (existing) | seconds–minutes | Yes (queues) |
| Cloud sync | Gateway worker → Npgsql UPSERT | minutes (OK to lag) | Yes (outbox grows) |

---

## Gateway

**"Gateway" is a role, not new hardware.** Three deployment options:

1. **Phase 1 (recommended start):** Install sync worker as a Windows service on the always-on register (usually Register 01). Cost: $0.
2. **Phase 2 (if reliability becomes a problem):** Dedicated mini-PC (~$150–300 Beelink/NUC/Pi 5) on the LAN. Independent of register uptime.
3. **Alternative:** Run on the NAS/SMB server if it can host .NET 10.

Move between hosts without touching registers — the SMB outbox is the contract.

---

## Components to Build

### 1. `Kasir.CloudSync` (new project)

New C# project in the solution. Windows service (or console app launched by Task Scheduler).

```
Kasir.CloudSync/
  Program.cs            — host + config
  OutboxReader.cs       — polls SMB outbox + local DB
  Mappers/
    ProductMapper.cs    — SQLite row → Product DTO → Postgres upsert
    SaleMapper.cs       — ditto per table
    ...one per synced table
  PostgresSink.cs       — Npgsql UPSERT with ON CONFLICT
  SyncCursor.cs         — tracks last-synced outbox sequence per table
  HealthCheck.cs        — reports last success, lag, error rate
```

Dependencies:
- `Npgsql` (latest stable for .NET 10)
- `Microsoft.Data.Sqlite` (already in Kasir.Core)
- `Microsoft.Extensions.Hosting` for service lifecycle

### 2. Litestream sidecar

Not a C# component. Separate binary installed on the gateway:
- Install Litestream Windows build
- Config file points at `kasir.db` on the gateway
- Destination: S3 / Backblaze B2 / Cloudflare R2 bucket
- Runs as Windows service, independent of `Kasir.CloudSync`

### 3. Supabase project

- New Supabase project (managed Postgres)
- Schema mirrors `Kasir/Data/Schema.sql` with drift fixes (see below)
- Service role key stored on gateway only — never on registers
- Enable row-level security as an extra guardrail even though only the service role writes

---

## Schema Drift Handling

Source: SQLite `Schema.sql` (57 tables). Target: Postgres. Handle these drift points explicitly:

| SQLite | Postgres | Notes |
|--------|----------|-------|
| `INTEGER` money (cents) | `BIGINT` | Rupiah cents exceed 2³¹ fast (21.5M IDR = 2.15B cents) |
| `INTEGER` boolean (0/1) | `BOOLEAN` | Mapper converts; or keep as `SMALLINT` if touching C# queries is too invasive |
| `TEXT` dates (ISO-8601) | `TIMESTAMPTZ` | Parse in mapper; watch for legacy FoxPro-migrated rows with inconsistent formats |
| `AUTOINCREMENT` rowid | `BIGINT` (not `SERIAL`) | We copy IDs verbatim, not generating new ones |
| Register-prefixed doc numbers (`KLR-01-2601-0001`) | `TEXT` PK | Already globally unique — use as natural keys |
| FTS5 virtual tables | **Skip** — rebuild with `tsvector` + GIN if search is needed in cloud |
| Soft FK (unenforced in SQLite) | Strict FK in Postgres | Initial load may fail on orphans — migrate FK-less, then add constraints after data-quality pass |

**Drift guardrail:** add `Atlas` or `migra` to CI to diff SQLite schema vs Postgres mirror schema. Failing check = update Postgres schema before merge.

**Mapper pattern:**

```csharp
// Kasir.CloudSync/Mappers/ProductMapper.cs
public static Product FromSqlite(SqliteDataReader r) => new Product
{
    Code = r.GetString(0),                         // TEXT → TEXT
    PriceCents = r.GetInt64(1),                    // INTEGER → BIGINT
    IsActive = r.GetInt32(2) == 1,                 // INTEGER 0/1 → BOOLEAN
    UpdatedAt = DateTime.Parse(r.GetString(3)),    // TEXT ISO → TIMESTAMPTZ
};
```

One mapper per table. Compilation breaks if the SQLite schema gains a column the mapper doesn't know about — that's the whole point.

---

## Initial Load vs Ongoing Sync

**Initial load** (one-time, per table):
- Use `pgloader` with explicit `CAST` directives for dates/booleans
- Run with FK constraints disabled
- Run data-quality pass (orphan FKs, NULL anomalies, out-of-range values)
- Enable FK constraints
- Verify row counts match

**Ongoing sync** (continuous):
- Gateway worker polls outbox every 30–60s
- Batch 100 rows per table per tick
- `INSERT ... ON CONFLICT (pk) DO UPDATE` — idempotent replay
- Mark outbox rows `cloud_synced=1` only after Postgres confirms
- Backoff on Postgres errors; never drop rows

---

## Outbox Extensions

Current outbox is LAN-scoped. Add two columns:

```sql
ALTER TABLE sync_outbox ADD COLUMN cloud_synced INTEGER NOT NULL DEFAULT 0;
ALTER TABLE sync_outbox ADD COLUMN cloud_synced_at TEXT NULL;
```

Gateway reads `WHERE cloud_synced = 0 ORDER BY id LIMIT 100`.
LAN sync ignores these columns. Independent bookkeeping per destination.

---

## Failure Modes

| Scenario | Behavior |
|---|---|
| Internet down | Registers + LAN sync unaffected. Outbox grows. Gateway retries with backoff. Catches up on reconnect. |
| Gateway down | Registers + LAN sync unaffected. No cloud updates. Rebuild gateway; it resumes from `cloud_synced=0` rows. |
| Register down | Others keep selling. Rebuilt register pulls SMB outbox; gateway already has its cloud copy. |
| SMB share down | Registers sell locally. LAN sync pauses. Fix NAS, resume. |
| Postgres corrupted | Restore mirror from Litestream S3 snapshot → rebuild Supabase by replaying outbox. |
| Supabase account lost | Switch connection string; replay from `cloud_synced=0` rows (reset flag for full rebuild). |

---

## Security

- Postgres connection string + service role key live only on gateway (Windows DPAPI or env var)
- Gateway → Supabase over TLS (Npgsql `SslMode=Require`)
- Litestream → S3 over TLS with IAM user scoped to one bucket
- Rotate keys quarterly
- No inbound ports opened — gateway only makes outbound connections
- `security-reviewer` agent must review before first prod deploy

---

## Rollout Phases

### Phase A — Read-only mirror of one table (1 week)
- Provision Supabase
- Build `Kasir.CloudSync` skeleton with only `products` table
- Prove end-to-end: SQLite change → Postgres visible within 60s
- Add Litestream to S3 in parallel (independent, no blocker)

### Phase B — Expand to core tables (2 weeks)
- Add mappers for `sales`, `sale_lines`, `stock`, `vendors`, `departments`
- Build Atlas/migra schema-diff CI check
- Data-quality pass on legacy rows (FK orphans, bad dates)

### Phase C — All 57 tables (2 weeks)
- Mappers for remaining tables
- Full initial load via pgloader + CAST
- Enable FK constraints in Postgres
- Verify row count parity

### Phase D — Observability + hardening (1 week)
- HealthCheck endpoint: last sync time, lag, error count per table
- Alert on lag > 15 minutes
- Postgres backup retention policy
- Litestream restore drill (actually restore and verify)

### Phase E — Consumers (after D stable)
- Supabase-native dashboards (sales today, stock low, top products)
- Phone-friendly view for owner
- (Later) Metabase connection for accountant

---

## Open Questions

- **Conflict resolution** — if cloud ever becomes writable, what wins? Deferred; keep cloud read-only for now.
- **PII / compliance** — are any customer fields in `sales` subject to data-residency rules? Check before choosing Supabase region.
- **Cost ceiling** — Supabase free tier is 500MB; 55MB DB + growth is fine for 1–2 years. Plan for Pro tier at ~$25/mo when hit.
- **Multi-store trigger** — if the owner opens a second store, revisit: each store gets its own gateway, all point at the same Supabase with a `store_id` column prefix. Do **not** retrofit multi-store until it's real.

---

## Explicit Non-Decisions (do later)

- Whether to eventually make Postgres the source of truth (no plan; probably never)
- Whether to add a web-app frontend (separate phase; orthogonal to sync)
- Whether to replace SMB with a proper sync service (works today; don't fix what's not broken)
