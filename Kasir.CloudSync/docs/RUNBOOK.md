# Kasir.CloudSync — Operations Runbook

Final ops guide covering day-to-day running of the Phase 6 cloud sync
pipeline. If you're reading this fresh, also skim:

- `PHASE-A-PROOF.md` — what the pipeline is
- `INITIAL-LOAD.md` — first-time provisioning
- `LITESTREAM-DRILL.md` — DR drill (monthly)
- `CAPACITY.md` — Supabase free-tier monitoring
- `SCHEMA-DRIFT.md` (in repo `docs/`) — adding new columns safely
- `gate-a0-2-litestream-decision.md` (in repo `plans/`) — Litestream
  install steps

This runbook is the single doc you should be able to follow at 4 AM.

---

## Architecture (one paragraph)

Three POS registers each have a local SQLite (`kasir.db`). Existing LAN
sync via SMB outbox keeps them in agreement. **Kasir.CloudSync** runs
as a Windows service on the gateway (Register 01 by default), polls
`sync_queue` for rows where `cloud_synced=0 AND status='synced'`, and
ships them to **Supabase Postgres (Singapore)** via parameterised
`INSERT ... ON CONFLICT DO UPDATE`. Independently, **Litestream**
streams the SQLite WAL to **Cloudflare R2** for byte-level disaster
recovery. The POS never talks to the cloud — internet failure cannot
block a sale.

---

## Daily / passive operation

You shouldn't need to do anything daily. The service runs, polls every
30 seconds, ships rows when they're available.

If you want to spot-check:

```cmd
:: Check the local /health endpoint on the gateway
curl http://localhost:5080/health
```

Or from any machine with Supabase access:

```sql
SELECT updated_at, payload->>'status' AS status,
       payload->>'outbox_depth' AS outbox_depth
FROM _sync_health WHERE id = 'current';
```

Healthy = `payload->>'status' = 'healthy'` and `updated_at` within the
last few minutes.

---

## Startup

The service is registered as a Windows service. Manual control:

```cmd
sc start Kasir.CloudSync
sc stop  Kasir.CloudSync
sc query Kasir.CloudSync
```

Logs go to the Windows Event Log under `Application` source
`Kasir.CloudSync`. Tail for live output: open Event Viewer ->
Windows Logs -> Application -> Filter Current Log -> Source =
Kasir.CloudSync.

For dev runs from the command line:

```cmd
cd C:\path\to\kasir-pos
dotnet run --project Kasir.CloudSync
```

---

## Shutdown / restart

```cmd
sc stop Kasir.CloudSync
:: Wait for service status STOPPED (1-2 seconds)
sc start Kasir.CloudSync
```

The worker checkpoints after every batch via `MarkCloudSynced`, so
resumes don't lose or duplicate rows.

If the service is unresponsive to `sc stop`:

```cmd
taskkill /IM Kasir.CloudSync.exe /F
sc start Kasir.CloudSync
```

A force-kill mid-batch is safe: the rows in flight stay
`cloud_synced=0`, the next tick picks them up, and the
`ON CONFLICT DO UPDATE` makes the retry idempotent.

---

## Alert response

The `/health` endpoint and `_sync_health` Supabase row include an
`alerts` array. Each alert has `severity` (`WARNING` / `CRITICAL`) and
`code`. Response per code:

| Code | Severity | Response |
|---|---|---|
| `LAG_WARNING` | WARNING | Likely transient. If persistent for >30 min, escalate to LAG_CRITICAL. |
| `LAG_CRITICAL` | CRITICAL | Cloud sync is >15 min behind. Check internet on gateway, check Supabase dashboard for outage, check `sc query Kasir.CloudSync`. |
| `OUTBOX_DEEP` | WARNING | Outbox >10K rows. Likely internet outage; will catch up on its own. Stop being concerned if the depth is decreasing. |
| `OUTBOX_FULL` | CRITICAL | Outbox >100K rows. Cloud sync should self-pause. Investigate why dispatch is failing — see logs. Risk: gateway disk fill. |
| `DB_GROWING` | WARNING | Supabase >400 MB. Run `_capacity_summary` to find the largest tables. Consider pruning or upgrading. |
| `DB_NEAR_CEILING` | CRITICAL | Supabase >475 MB. INSERTs will start failing soon. Either prune (carefully) or upgrade to Pro within days. |
| `ERROR_RATE` | WARNING | >10 errors per 5 min on a single table. Check Event Log for the actual exception — usually a column-type drift. |

A critical alert that persists >24h = open an incident; the local POS
is unaffected but the cloud mirror is no longer trustworthy.

---

## Capacity check (monthly)

```sql
-- Supabase psql
SELECT * FROM _capacity_total;
SELECT * FROM _capacity_summary LIMIT 10;
SELECT * FROM _trgm_status;
```

Record `_capacity_total.bytes_total` in a calendar entry. After 4 weeks
of samples, compute weekly growth and run
`CapacityMonitor.ProjectedDaysUntilCeiling`. Any projection under 90
days = act now.

---

## Key rotation (quarterly)

1. **Supabase service-role key**:
   - Supabase dashboard -> Project Settings -> API -> Service Role -> Reveal
   - Generate new -> copy
   - On gateway: re-encrypt via DPAPI (the Kasir.CloudSync first-run
     prompt; or run `dotnet run --project Kasir.CloudSync -- --reset-credentials`)
   - Restart service: `sc stop && sc start Kasir.CloudSync`
   - Verify next /health request shows healthy
   - In Supabase dashboard, revoke the old key

2. **Cloudflare R2 token** (Litestream):
   - Cloudflare dashboard -> R2 -> Manage API Tokens -> Create
   - Update `C:\ProgramData\Litestream\litestream.yml`
   - `sc stop Litestream && sc start Litestream`
   - Run a drill (`Tools/LitestreamDrill/restore-and-verify.ps1`) to
     confirm the new token works
   - Revoke the old token

3. **HMAC sync key** (LAN sync between registers): not part of cloud
   sync. Documented in the legacy sync runbook; rotation requires
   coordinated restart of all 3 registers + hub.

---

## Adding a new column to a synced table

This is the most common change after first install. Procedure:

1. Add the column to `Kasir.Core/Data/Schema.sql` (the SQLite source).
2. Run the matching `ALTER TABLE` on every register's `kasir.db`
   (sync_queue style: schema first, code second).
3. Add the column to `Kasir.CloudSync/Sql/{table}.sql`.
4. Add the column to the matching `TableMapping` in
   `Kasir.CloudSync/Generation/TableMappings.cs`.
5. Apply the Postgres `ALTER TABLE` to Supabase.
6. Push. Schema-drift CI must pass.
7. Rebuild + redeploy `Kasir.CloudSync`.

---

## Adding a brand new mirrored table

1. Add the new table to `Kasir.Core/Data/Schema.sql`.
2. Add it to `Kasir.Core.Sync.SyncConfig.SyncedTables` (the LAN sync
   trigger registry).
3. Write `Kasir.CloudSync/Sql/{table}.sql` (Postgres DDL).
4. Add a `TableMapping` to `Kasir.CloudSync/Generation/TableMappings.cs`
   and register it in `TableMappings.All`.
5. Add `{table}` to the `TABLES` array in
   `scripts/check-schema-drift.sh`.
6. Add to `InitialLoader.LoadOrder` in dependency-safe position.
7. Push. CI passes. Run `--initial-load` to backfill the new table.

---

## Gateway hardware failure recovery

If the gateway machine (Register 01 by default) dies:

1. Designate replacement hardware (another register or a fresh mini-PC).
2. Install .NET 10 runtime + `sqlite3` CLI.
3. Restore `kasir.db`:
   - **Option A (preferred):** Pull from latest LAN sync —
     the SMB outbox has the data, just point the new machine at it.
   - **Option B:** Restore from Litestream WAL via
     `Tools/LitestreamDrill/restore-and-verify.ps1` (run with the
     existing R2 credentials; the `-o` parameter writes the recovered
     DB to a chosen path).
4. Install Litestream on the new machine; copy the `litestream.yml` from
   a backup, set `R2_ACCESS_KEY_ID` + `R2_SECRET_ACCESS_KEY` env vars.
5. Install Kasir.CloudSync; first run will prompt for the Supabase
   service-role key (DPAPI is machine-bound, so the encrypted credential
   on the dead machine is unrecoverable; fetch the key from your
   password manager).
6. `sc start Kasir.CloudSync` — first tick picks up everything that
   was in flight.
7. Run a Litestream drill to confirm the new gateway's WAL replication
   is healthy. Don't skip this — if step 4 was misconfigured we want
   to know now.

Until step 6 completes, **the local SQLite is the only authoritative
data source**. Owners reading the Supabase mirror should be told it is
stale.

---

## Deferred / out of scope

- **Kasir.WebApi (Track B):** local read-only REST API on the hub.
  Gated on Phase A 1-week stability + Phase D health green for a week.
  Not required for cloud sync to function.
- **Hard delete in Postgres mirror:** Phase A chose soft-delete via
  `status='D'`. Hard delete deferred until ops experience tells us
  whether the audit trail is worth more than the storage.
- **Per-register cloud filtering:** every register's outbox ships to
  the same Supabase tables. If multi-store ever happens, add a
  `store_id` column and migrate. Don't pre-build it.

---

## Smoke test for a new operator

These four commands prove the pipeline is alive end-to-end:

```cmd
:: 1. Service is running
sc query Kasir.CloudSync

:: 2. Local health endpoint responds
curl http://localhost:5080/health

:: 3. Litestream service running
sc query Litestream

:: 4. Remote health row is fresh
:: (run in any psql / Supabase Studio session)
:: SELECT updated_at FROM _sync_health WHERE id='current';
:: -> within last 2 minutes = healthy
```

If all four are green, you can leave it alone.
