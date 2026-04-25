# NEXT STEPS — Cloud Sync (Windows 10 gateway operator handoff)

**Target audience:** an agent or operator with shell access on the
Windows 10 gateway machine (Register 01, by default). Steps that were
doable from a dev laptop are already done; everything below requires
the actual gateway hardware + the kasir.db files on the registers.

---

## What's already done (don't redo)

| | Status | Evidence |
|---|---|---|
| Supabase project provisioned | ✅ | Project ref `mnatezzsysmadvrosnad`, region `ap-southeast-1` (Singapore), free tier |
| All 19 mirror tables + 3 capacity views + `_sync_health` table | ✅ | Created via `Kasir.CloudSync/Sql/*.sql` over the Supavisor pooler |
| Initial bulk load | ✅ | 89,608 rows in 35 seconds (24,560 products / 26,407 cash_transactions / 25,146 stock_movements / 12,421 purchases / 754 subsidiaries / etc.) — all parity OK |
| `pg_trgm` extension + GIN index on `products.search_text` | ✅ | Verified live: `word_similarity('NIVEA', search_text)` returns 5 NIVEA products in <50 ms |
| FK constraints (4 of 6) | ✅ | `fk_product_barcodes_product`, `fk_sale_items_journal`, `fk_sale_items_product`, `fk_orders_sub`. Two deferred (see "Known caveats" below). |
| Code (PR #15) | ✅ | 17 commits on `feat/cloud-sync-and-local-api`, 298 + 38 = 336 tests passing |

If you re-run any of these, you'll truncate and re-load the cloud
mirror — disruptive, not catastrophic. Recovery recipe is at the
bottom of `docs/LIVE-LOAD-RESULTS.md`. Better to leave them alone.

---

## What you need from the human first

The Supabase password and service-role key are NOT in git — they live
in `kasir-pos/.env` on the dev laptop only. Ask the human to paste them
or copy `.env` to your working directory. You need:

- `CONNECTION_STRING` (Postgres URL via Supavisor pooler, port 5432, session mode)
- `CONNECTION_STRING_TX` (port 6543, transaction mode — for the worker)
- `SERVICE_ROLE_KEY` (JWT, used only at the Supabase API level — not at Postgres level)

Also, the file `kasir-pos/appsettings.json` (gitignored) holds an
older direct-connection format. Don't use it; use the pooler URLs in
`.env`. The pooler is mandatory because the free-tier direct DB host
is IPv6-only and most gateways are IPv4-only.

---

## Known caveats (read before doing anything)

1. **IPv6-only direct connection.** `db.mnatezzsysmadvrosnad.supabase.co`
   has no A record, only AAAA. If the gateway has IPv6 connectivity,
   direct works. If not, **always use the Supavisor pooler at
   `aws-1-ap-southeast-1.pooler.supabase.com`**:
   - port 5432 = session mode (use for DDL / one-shot queries)
   - port 6543 = transaction mode (use for the steady-state worker)
   - Username on the pooler is `postgres.mnatezzsysmadvrosnad`, NOT
     plain `postgres`.
2. **Two FK constraints intentionally deferred:**
   - `fk_purchases_sub` — some legacy purchases have empty-string
     `sub_code`. Postgres treats `''` as a non-null value missing
     from the parent table, so the FK rejects them. Do not retry this
     constraint without first cleaning or NULL-ifying empty strings.
   - `fk_stock_movements_product` — 13,075 history rows reference
     deleted products (FoxPro migration leftover). Acceptable to leave
     unenforced; this is historical immutable data.
3. **`sales` table is empty in the source `data/kasir.db`.** The
   live-load run used a development snapshot. Once the worker starts
   shipping real LAN-sync rows, `sales` and `sale_items` populate
   naturally.
4. **The drift CI guard** (`scripts/check-schema-drift.sh`) runs in CI
   on every PR — if you alter a `Sql/*.sql` file, expect the check to
   complain unless you also adjust `Generation/TableMappings.cs` to
   match.

---

## Sequence (do these in order, with checklists)

### 1. Run Gate A0.1 TLS smoke test [~5 min]

Confirms the gateway can negotiate TLS to Supabase before we commit to
any of the heavier work.

- [ ] On the **dev laptop** (already done if you got the build from
      git): build the smoke test for win-x64
  ```bash
  cd kasir-pos/Tools/TlsSmokeTest
  dotnet publish -c Release -r win-x64 --self-contained true -o ./publish
  ```
- [ ] Copy `./publish/` to the Windows 10 gateway. The folder contains
      `TlsSmokeTest.exe` plus the .NET 10 runtime — nothing to install.
- [ ] On the gateway, set the **pooler** connection string (not the
      direct one) and run:
  ```cmd
  setx SUPABASE_CONN_STRING "Host=aws-1-ap-southeast-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.mnatezzsysmadvrosnad;Password=<paste from .env>;SslMode=Require"
  :: open a NEW cmd window so setx takes effect
  cd publish
  TlsSmokeTest.exe
  ```
- [ ] Confirm the last line of output is `GATE A0.1: PASS`. If
      it isn't, see `Tools/TlsSmokeTest/README.md` for failure
      triage. Most common failure on Windows 10 is firewall egress
      blocked on port 5432; not a TLS issue.
- [ ] Capture the negotiated TLS protocol + cipher (last query in the
      output) and append to `plans/phase-a-preflight-results.md`.
      Stop here and ping the human if the test fails — don't proceed.

### 2. Apply Gate A0.3 sync_queue migration [~30 min, plan a downtime window]

The cloud worker queries `sync_queue` for rows with `cloud_synced = 0`,
a column that doesn't exist on the current registers. The migration
adds that column plus a sibling timestamp, and expands the
`table_name` CHECK constraint to allow `discount_partners` and
`credit_cards` (which `SyncConfig.SyncedTables` already lists in code).

#### Minimum-viable scope (recommended)

The cloud worker only reads from the **gateway's local kasir.db**
(Register 01 in this deployment). You only have to migrate that one
file for cloud sync to function. The other two registers can keep
their old 11-column `sync_queue` indefinitely:

- The new `Kasir.Core.dll` reads `cloud_synced` defensively via
  `SqlHelper.GetInt`, which returns `0` when the column is missing.
  No crash on Reg 02/03.
- `GetPendingCloud` and `MarkCloudSynced` would fail at SQL parse on
  the old schema, but those methods are only called by the cloud
  worker, which only runs on Register 01.
- LAN sync code paths (`GetPending`, `MarkSynced`, `MarkFailed`,
  `GetMaxId`, `PruneSynced`) work unchanged on the old schema.
- There are no triggers in the codebase for `discount_partners` or
  `credit_cards`, so the unexpanded CHECK constraint on Reg 02/03
  isn't currently exercised. If you ever add such a trigger later,
  you must migrate Reg 02/03 first.

So the minimum recipe is: migrate Register 01 only. Defer the other
two indefinitely. Ask the human if they prefer the full 4-DB recipe;
both are documented.

#### Full recipe (Register 01 first; Reg 02/03 same way later when convenient)

Find Register 01's `kasir.db`. Default location is
`C:\kasir\data\kasir.db`. If unsure, check the running POS for the
DB path in `Help → About` or run:
```cmd
sqlite3 C:\kasir\data\kasir.db "SELECT 1;"
```
to confirm the file.

- [ ] **Backup the file.** Plug a USB stick or pick a recovery folder:
  ```cmd
  copy C:\kasir\data\kasir.db D:\backup\kasir-PRE-MIGRATION-%date:~0,4%%date:~5,2%%date:~8,2%.db
  ```
  This is your only rollback path. Do not skip.
- [ ] Drain pre-condition. The migration needs `sync_queue` empty of
      pending or failed work:
  ```cmd
  sqlite3 C:\kasir\data\kasir.db "SELECT COUNT(*) FROM sync_queue WHERE status IN ('pending','failed');"
  ```
  Expect `0`. If non-zero, you have two options:
  - Wait for the next LAN sync cycle to drain `pending` rows.
  - For old `failed` rows that will never resolve: capture them
    first, then delete:
    ```cmd
    sqlite3 C:\kasir\data\kasir.db ".dump sync_queue" > D:\backup\sync_queue-failed-rows.sql
    sqlite3 C:\kasir\data\kasir.db "DELETE FROM sync_queue WHERE status='failed';"
    ```
  Document the disposition in `plans/phase-a-preflight-results.md`
  and continue.
- [ ] Stop the POS application on Register 01. The migration runs
      while no SQLite writer is active.
- [ ] Apply the migration:
  ```cmd
  sqlite3 C:\kasir\data\kasir.db < kasir-pos\Kasir.CloudSync\Sql\001_sync_queue_recreate.sql
  ```
  The script wraps everything in `BEGIN/COMMIT`. Either it all lands
  or none of it does.
- [ ] Verify column count (expected: 13):
  ```cmd
  sqlite3 C:\kasir\data\kasir.db "PRAGMA table_info(sync_queue);"
  ```
- [ ] Verify the new CHECK accepts `discount_partners`:
  ```cmd
  sqlite3 C:\kasir\data\kasir.db "INSERT INTO sync_queue (register_id, table_name, record_key, operation) VALUES ('TEST','discount_partners','X','I'); DELETE FROM sync_queue WHERE register_id='TEST';"
  ```
  No error = success.
- [ ] Restart the POS app. Make a test sale and confirm it syncs to
      the hub via the existing SMB outbox (Reg 02/03 should see it
      via their normal Pull cycle).

If anything goes wrong, restore from the USB backup file. The local
POS doesn't depend on the cloud at any point during this.

### 3. Install Litestream on the gateway [~15 min]

Litestream streams the SQLite WAL to Cloudflare R2 for byte-level
disaster recovery. Independent of `Kasir.CloudSync` — runs as its own
Windows service.

- [ ] Provision a Cloudflare R2 bucket (free tier — 10 GB):
  - Create bucket named `kasir-litestream` (or any name; record it).
  - Create an API token scoped to **only this bucket**, with
    `Workers R2 Storage:Edit` permission.
  - Save access key id + secret in your password manager.
  - Note the **account ID** from the R2 dashboard URL.
- [ ] Download the pinned binary on the gateway:
  ```powershell
  $url = "https://github.com/benbjohnson/litestream/releases/download/v0.5.11/litestream-0.5.11-windows-x86_64.zip"
  Invoke-WebRequest -Uri $url -OutFile "$env:TEMP\litestream.zip"
  (Get-FileHash "$env:TEMP\litestream.zip" -Algorithm SHA256).Hash
  # Expect: 9116E8605D4B479E15044CCBCCF2AA756BE7A8A64B9237F67A074EC1742444A3
  ```
- [ ] Extract:
  ```powershell
  Expand-Archive -Path "$env:TEMP\litestream.zip" -DestinationPath "C:\Program Files\Litestream"
  ```
- [ ] Create `C:\ProgramData\Litestream\litestream.yml`:
  ```yaml
  dbs:
    - path: C:\kasir\data\kasir.db
      replicas:
        - type: s3
          bucket: kasir-litestream
          path: kasir
          endpoint: https://<R2_ACCOUNT_ID>.r2.cloudflarestorage.com
          access-key-id: ${R2_ACCESS_KEY_ID}
          secret-access-key: ${R2_SECRET_ACCESS_KEY}
          force-path-style: true
  ```
  Replace `<R2_ACCOUNT_ID>`. Lock the file ACL to the service account
  only (otherwise any user could read R2 credentials):
  ```powershell
  icacls "C:\ProgramData\Litestream\litestream.yml" /inheritance:r /grant:r "SYSTEM:(R)" "Administrators:(R)"
  ```
- [ ] Set the env vars on the service account (system-wide):
  ```powershell
  [Environment]::SetEnvironmentVariable("R2_ACCESS_KEY_ID", "...", "Machine")
  [Environment]::SetEnvironmentVariable("R2_SECRET_ACCESS_KEY", "...", "Machine")
  ```
- [ ] Register as a Windows service via NSSM (recommended) or
      `sc create`. NSSM is easier:
  ```powershell
  # download NSSM if not present, then:
  nssm install Litestream "C:\Program Files\Litestream\litestream.exe" "replicate -config C:\ProgramData\Litestream\litestream.yml"
  nssm set Litestream Start SERVICE_AUTO_START
  nssm start Litestream
  ```
- [ ] Tail the log briefly to confirm it's replicating:
  ```cmd
  nssm get Litestream AppStdout
  type <whatever-path-it-tells-you>
  ```
  Expect lines like `replica is up to date` within a minute.
- [ ] Verify a restore round-trip BEFORE moving on. Use the script:
  ```powershell
  cd kasir-pos\Tools\LitestreamDrill
  powershell -ExecutionPolicy Bypass -File .\restore-and-verify.ps1
  ```
  Exit 0 = the backup actually round-trips. If it fails, fix
  Litestream before continuing — a backup that has never been
  restored is not a backup.

### 4. Install + start `Kasir.CloudSync` service [~10 min]

The cloud sync worker. Runs as a Windows service on the gateway.
Polls `sync_queue` for `cloud_synced=0 AND status='synced'` rows
every 30s, ships them to Supabase via parameterised UPSERT.

- [ ] On the dev laptop, publish self-contained for Windows:
  ```bash
  cd kasir-pos
  dotnet publish Kasir.CloudSync -c Release -r win-x64 --self-contained true -o ./publish-cloudsync
  ```
- [ ] Copy `./publish-cloudsync/` to the gateway, e.g.
      `C:\Program Files\Kasir.CloudSync\`.
- [ ] Provide the Supabase **transaction-mode** pooler URL (port 6543)
      via env var or `appsettings.json`. The transaction pooler is more
      efficient for the worker's many short queries:
  ```powershell
  [Environment]::SetEnvironmentVariable(
      "KASIR_CLOUDSYNC_SUPABASE",
      "Host=aws-1-ap-southeast-1.pooler.supabase.com;Port=6543;Database=postgres;Username=postgres.mnatezzsysmadvrosnad;Password=<from .env>;SslMode=Require",
      "Machine")
  [Environment]::SetEnvironmentVariable(
      "KASIR_CLOUDSYNC_DBPATH",
      "C:\kasir\data\kasir.db",
      "Machine")
  ```
  (For a real production deploy, prefer DPAPI-encrypted credentials
  via `ProtectedData.Protect` — wire that in once before merging.
  For initial testing, env vars are fine.)
- [ ] Register as Windows service:
  ```cmd
  sc create Kasir.CloudSync binPath= "\"C:\Program Files\Kasir.CloudSync\Kasir.CloudSync.exe\"" start= auto
  sc start Kasir.CloudSync
  ```
- [ ] Verify health from the gateway browser:
      `http://localhost:5080/health`
      Expected: JSON with `"status": "healthy"`, an `outbox_depth`
      number, per-table `last_sync_utc`, and (if Supabase is
      reachable) `supabase_db_size_mb`.
- [ ] Tail the Windows Event Log under `Application` for source
      `Kasir.CloudSync`. First lines should be:
      ```
      CloudSync worker started
      ```

### 5. End-to-end pipeline verification [~5 min]

- [ ] Make a sale on Register 02 (or Register 01 directly).
- [ ] Within 60 seconds, query Supabase from the dev laptop or
      anywhere with the connection string:
  ```sql
  SELECT id, journal_no, doc_date, total_value, register_id
  FROM sales
  ORDER BY id DESC
  LIMIT 5;
  ```
  Your test sale should be there.
- [ ] Query the remote health row:
  ```sql
  SELECT updated_at, payload->>'status' AS status,
         payload->>'outbox_depth' AS outbox_depth
  FROM _sync_health WHERE id='current';
  ```
  `updated_at` should be within the last minute, `status` healthy.
- [ ] Verify pg_trgm search works:
  ```sql
  -- Use word_similarity (<%>), not full-string (%); see CAPACITY.md
  SELECT product_code, name, word_similarity('NIVEA', search_text) AS ws
  FROM products
  WHERE 'NIVEA' <% search_text
  ORDER BY ws DESC
  LIMIT 5;
  ```
  Expect 5 NIVEA products in <50 ms.

### 6. security-reviewer sign-off [~30 min]

The cloud-sync plan requires a security review before first
production deploy. From a Claude Code session on the dev laptop with
the branch checked out:

```
@oh-my-claudecode:security-reviewer review the cloud-sync changes on
this branch. Scope: credential storage (DPAPI vs env-var fallback),
TLS configuration (pooler SslMode=Require), connection-string
handling, error messages (no leaked secrets in logs), HMAC invariance
on sync_queue, R2 IAM token scoping, and the service-role key
exposure surface.
```

Address any flagged issues before running unattended for a week. The
existing tests cover HMAC invariance (`PushServiceHmacInvarianceTests`)
and the sink/loader correctness — what the security reviewer adds
is the credential-handling adversarial pass.

### 7. 1-week stability watch [7 days]

- [ ] Daily: glance at `_sync_health` from your phone:
  ```sql
  SELECT updated_at, payload->>'status', payload->>'alerts'
  FROM _sync_health WHERE id='current';
  ```
  `status` should be `healthy`. Empty `alerts` array.
- [ ] If any `CRITICAL` alert fires, follow the per-code response in
      `Kasir.CloudSync/docs/RUNBOOK.md`.
- [ ] At the end of week 1, run the Litestream drill again to make
      sure WAL replication is still healthy:
      `Tools/LitestreamDrill/restore-and-verify.ps1`
- [ ] After 7 days of green and a successful drill: ready to merge
      PR #15 to `main`.

### 8. Track B (later, separate PR)

The local read-only `Kasir.WebApi` project mentioned in the master
plan is **not in PR #15**. It's deliberately deferred until after
this 1-week stability watch because it adds another workload to
Register 01. When ready, open a follow-up PR; the plan section
"Track B" describes the scope.

---

## Quick reference

| Need to... | Doc |
|---|---|
| See exactly what was done in the live load | `docs/LIVE-LOAD-RESULTS.md` |
| Understand the full architecture | `Kasir.CloudSync/docs/PHASE-A-PROOF.md` |
| Decode an alert code | `Kasir.CloudSync/docs/RUNBOOK.md` |
| Drill the disaster recovery | `Kasir.CloudSync/docs/LITESTREAM-DRILL.md` |
| Check capacity / pg_trgm tips | `Kasir.CloudSync/docs/CAPACITY.md` |
| Add a new column to a synced table | `docs/SCHEMA-DRIFT.md` |
| Decide on Litestream install path | `plans/gate-a0-2-litestream-decision.md` |
| Re-run the bulk load | `Kasir.CloudSync/docs/INITIAL-LOAD.md` |

---

## Rollback (if anything goes wrong in steps 2–4)

The local POS keeps working at every step — selling never depends on
any of this. Recovery sequence:

1. `sc stop Kasir.CloudSync` (if the service is running)
2. `sc stop Litestream` (if installed)
3. Restore Register 01's `kasir.db` from your USB backup (step 2)
4. Restart the POS app on Register 01 and confirm LAN sync still
   works (make a sale on Reg 02, see it appear on Reg 01)
5. Either drop the Supabase project (Settings → General → Delete
   project) or just leave it — the data isn't authoritative for
   anything

The `Kasir.Core.dll` you deployed in step 2 is forward- and backward-
compatible: it works against both the old and new `sync_queue`
schemas. You can leave the new DLL in place and re-migrate later, or
roll it back to the previous DLL — neither breaks anything.

---

## Surprises / questions

If any step fails in a way the runbook doesn't cover, capture:

- The exact command + full stdout/stderr
- `sc query Kasir.CloudSync` output
- `curl http://localhost:5080/health` output
- The current `kasir.db` schema: `sqlite3 kasir.db "PRAGMA table_info(sync_queue);"`
- `gh pr view 15 --json title,body | jq` for the PR context

…and ping the human or open an issue against PR #15. The pipeline is
designed so partial failures don't compound — a broken cloud sync
never breaks selling.
