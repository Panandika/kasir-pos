# NEXT STEPS — Cloud Sync + Local API

> **Update 2026-04-25:** Steps 1, 2, and 6 are DONE on real Supabase
> (project `mnatezzsysmadvrosnad`, ap-southeast-1). 89,608 rows
> mirrored. 4 of 6 FK constraints applied; the remaining 2
> (`fk_purchases_sub`, `fk_stock_movements_product`) are deferred
> behind legacy-data cleanup. Remaining steps (3, 4, 5, 7, 8, 9, 10)
> still need Win10 hardware. See `LIVE-LOAD-RESULTS.md` next to this
> file for the run summary.

What's done in the repo (PR #15) vs what you need to do **on actual
hardware / cloud accounts** to take the pipeline to production.

> Code state: 15 commits on `feat/cloud-sync-and-local-api`,
> 298 + 38 = **336 tests passing**, 19 mirror tables column-aligned,
> draft PR #15 open.

The work below is the operator path — it cannot be done from a dev
laptop. Everything in the repo is ready and waiting.

---

## Sequence (do these in order)

### 1. Provision the Supabase project [~10 min]

- [X] Sign up / log in at https://supabase.com
- [X] Create a new project:
  - **Region:** Singapore (`ap-southeast-1`) — closest to Indonesia
  - **DB password:** generate a strong one; **save it in your password
    manager** (DPAPI on the gateway is machine-bound — losing the
    password is recoverable only via this copy)
  - Plan: **Free tier** (500 MB cap is fine for 1–2 years per the
    capacity projection in `Kasir.CloudSync/docs/CAPACITY.md`)
- [X] From `Project Settings → Database → Connection string → .NET`,
      copy the connection string. It looks like:
      `Host=db.PROJECT.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=...;SslMode=Require` > check the kasir-pos/.env
- [X] Save the **service-role key** (not the anon key) from
      `Project Settings → API → service_role` — you'll DPAPI-encrypt this
      on the gateway later.

### 2. Apply all DDL to Supabase [~5 min]

From `kasir-pos/`:

```bash
export PGURL="$(your full connection string from step 1)"

# Order matters: parents first
for t in departments accounts locations credit_cards subsidiaries members \
         products product_barcodes discounts discount_partners \
         purchases sales sale_items \
         cash_transactions memorial_journals orders \
         stock_transfers stock_adjustments stock_movements; do
    psql "$PGURL" -f "Kasir.CloudSync/Sql/$t.sql"
done

# Capacity helpers + remote health row + pg_trgm verification view
psql "$PGURL" -f Kasir.CloudSync/Sql/E_capacity_monitoring.sql
psql "$PGURL" -f Kasir.CloudSync/Sql/_sync_health.sql
```

DO NOT apply `constraints.sql` yet — the loader does it after the bulk
load + parity check.

### 3. Run Gate A0.1 TLS smoke test [~5 min, on Win10 hardware]

- [ ] Build the smoke test on macOS/Linux:
  ```bash
  cd kasir-pos/Tools/TlsSmokeTest
  dotnet publish -c Release -r win-x64 --self-contained true -o ./publish
  ```
- [ ] Copy `./publish/` to the target Windows 10 gateway
- [ ] On Windows:
  ```cmd
  setx SUPABASE_CONN_STRING "Host=db.PROJECT.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=...;SslMode=Require"
  :: open a NEW cmd window so setx takes effect
  cd publish
  TlsSmokeTest.exe
  ```
- [ ] Confirm last line is `GATE A0.1: PASS`. If it fails, see the
      failure-path triage in `Tools/TlsSmokeTest/README.md`.
- [ ] Record the test outcome in `plans/phase-a-preflight-results.md`
      (timestamp, target hostname, Win10 build, TLS version + cipher
      from the output).

### 4. Apply Gate A0.3 sync_queue migration [~30 min, coordinated downtime]

This is the riskiest step. Plan an end-of-business-day window because
all 4 databases need the same migration in the same window.

- [ ] **Backup every kasir.db first.** Copy each register's plus the
      hub's `kasir.db` to a USB stick. Required for rollback.
- [ ] Drain pre-condition on every register:
  ```bash
  sqlite3 kasir.db "SELECT COUNT(*) FROM sync_queue WHERE status IN ('pending','failed');"
  # must be 0; resolve any 'failed' rows manually (delete or retry) first
  ```
- [ ] Apply the migration on each of the 4 databases:
  ```bash
  sqlite3 kasir.db < Kasir.CloudSync/Sql/001_sync_queue_recreate.sql
  sqlite3 kasir.db "PRAGMA table_info(sync_queue);"
  # confirm 13 columns
  ```
- [ ] Deploy the new `Kasir.Core.dll` (with `cloud_synced` columns in
      `MapEntry` + `SyncQueueEntry`) to all 3 registers + hub.
- [ ] Spot-check LAN sync still works: make a sale on Register 02,
      confirm it syncs to the hub via the existing SMB outbox.

### 5. Install Litestream on the gateway [~15 min]

- [ ] Download the pinned binary:
  ```powershell
  $url = "https://github.com/benbjohnson/litestream/releases/download/v0.5.11/litestream-0.5.11-windows-x86_64.zip"
  Invoke-WebRequest -Uri $url -OutFile "$env:TEMP\litestream.zip"
  (Get-FileHash "$env:TEMP\litestream.zip" -Algorithm SHA256).Hash
  # Expect: 9116E8605D4B479E15044CCBCCF2AA756BE7A8A64B9237F67A074EC1742444A3
  ```
- [ ] Extract to `C:\Program Files\Litestream\`
- [ ] Provision a Cloudflare R2 bucket (free tier — 10 GB):
  - Create R2 bucket named e.g. `kasir-litestream`
  - Create an API token scoped to **only this bucket**, with
    `Workers R2 Storage:Edit` permission
  - Save the access key id + secret in your password manager
- [ ] Create `C:\ProgramData\Litestream\litestream.yml`:
  ```yaml
  dbs:
    - path: C:\kasir\data\kasir.db
      replicas:
        - type: s3
          bucket: kasir-litestream
          path: kasir
          endpoint: https://<ACCOUNT_ID>.r2.cloudflarestorage.com
          access-key-id: ${R2_ACCESS_KEY_ID}
          secret-access-key: ${R2_SECRET_ACCESS_KEY}
          force-path-style: true
  ```
- [ ] Set the env vars on the gateway service account
- [ ] Register as a Windows service via NSSM (recommended) or
      `sc create`. See `plans/gate-a0-2-litestream-decision.md` for the
      full recipe.

### 6. Run the initial load [~5–15 min]

On the gateway, with `KASIR_CLOUDSYNC_SUPABASE` and
`KASIR_CLOUDSYNC_DBPATH` env vars set:

```cmd
dotnet run --project Kasir.CloudSync -- --initial-load
```

Expected output:
- Orphan scan: per-check `clean` (or warnings + abort)
- 19 tables loaded with parity OK
- `FK constraints applied from .../Sql/constraints.sql`

If orphans are flagged: clean the SQLite source first, OR re-run with
`--skip-orphans` and accept the FK failures will surface at the end.

### 7. Install + start `Kasir.CloudSync` service [~10 min]

- [ ] Publish self-contained for Windows:
  ```bash
  dotnet publish Kasir.CloudSync -c Release -r win-x64 --self-contained true -o ./publish-cloudsync
  ```
- [ ] Copy `publish-cloudsync/` to gateway, e.g. `C:\Program Files\Kasir.CloudSync\`
- [ ] Encrypt the Supabase connection string via DPAPI on first run
      (the worker prompts for it)
- [ ] Register as Windows service:
  ```cmd
  sc create Kasir.CloudSync binPath= "C:\Program Files\Kasir.CloudSync\Kasir.CloudSync.exe" start= auto
  sc start Kasir.CloudSync
  ```
- [ ] Verify health from any browser on the gateway:
      `http://localhost:5080/health` should return JSON with
      `"status": "healthy"`

### 8. Verify the full pipeline end-to-end [~5 min]

- [ ] Make a sale on Register 02
- [ ] Within 60 seconds, query Supabase:
      `SELECT * FROM sales WHERE doc_date = CURRENT_DATE ORDER BY id DESC LIMIT 5;`
      The new sale should be there.
- [ ] Query the remote health row:
      `SELECT updated_at, payload->>'status' FROM _sync_health WHERE id='current';`
      `updated_at` should be within the last minute, `status` healthy.
- [ ] Run the Litestream drill:
      `Tools/LitestreamDrill/restore-and-verify.ps1` should exit 0.

### 9. security-reviewer sign-off [~30 min]

The plan requires a security review before first prod deploy. Run:

```
@oh-my-claudecode:security-reviewer review the cloud-sync changes on
this branch. Scope: credential storage (DPAPI), TLS configuration,
connection string handling, error messages (no leaked secrets in logs),
HMAC invariance, R2 IAM scoping.
```

Address any flagged issues before next step.

### 10. 1-week stability watch

- [ ] Daily: glance at `_sync_health` row from your phone — `status`
      should stay `healthy`
- [ ] If any CRITICAL alert fires, the runbook
      (`Kasir.CloudSync/docs/RUNBOOK.md`) has per-code response
- [ ] After 7 days of green: ready to merge PR #15 to `main`

### 11. Track B (later, separately)

The local `Kasir.WebApi` project mentioned in the plan is **not in this
PR**. It's deferred until after the 1-week stability watch above
because it adds another workload to Register 01. When you're ready,
open a follow-up PR; the plan section "Track B" describes the scope.

---

## Quick reference

| Need to... | Doc |
|---|---|
| Understand the architecture | `Kasir.CloudSync/docs/PHASE-A-PROOF.md` |
| Run the initial bulk load | `Kasir.CloudSync/docs/INITIAL-LOAD.md` |
| Decode an alert | `Kasir.CloudSync/docs/RUNBOOK.md` |
| Drill the disaster recovery | `Kasir.CloudSync/docs/LITESTREAM-DRILL.md` |
| Check capacity / pg_trgm | `Kasir.CloudSync/docs/CAPACITY.md` |
| Add a new column to a synced table | `docs/SCHEMA-DRIFT.md` |
| Decide on Litestream install path | `plans/gate-a0-2-litestream-decision.md` |

---

## Rollback (if you need to abort)

If anything in steps 4–7 goes wrong:

1. Stop `Kasir.CloudSync` service (`sc stop Kasir.CloudSync`)
2. Stop Litestream service
3. Restore `kasir.db` on each register from your USB backup (step 4)
4. Confirm LAN sync still works via existing flow
5. Drop the Supabase project (or just stop using it; data stays
   queryable for a while if you change your mind)

The local POS is unaffected at every step — selling never depends on
any of this.

---

## Questions / surprises

If a step fails in a way the runbook doesn't cover, capture:
- The exact command run + full error output
- `gh pr view 15 --json title,body | jq` for context
- Output of `sc query Kasir.CloudSync` and `curl http://localhost:5080/health`

…and either open an issue against the branch or pause and ping for
help. The pipeline is designed so partial failures don't compound — a
broken cloud sync never breaks selling.
