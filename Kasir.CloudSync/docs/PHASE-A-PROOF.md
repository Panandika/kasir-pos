# Phase A Proof of Readiness

**Status:** Green on macOS dev; awaiting Win10 hardware + live Supabase for final close.
**Branch:** `feat/cloud-sync-and-local-api`
**Date:** 2026-04-25

## What passed (9 stories green in-repo)

| Story | Evidence |
|---|---|
| US-P0   | Branch + draft PR #15 on `Panandika/kasir-pos` |
| US-A0-3 | `Kasir.CloudSync/Sql/001_sync_queue_recreate.sql` applied cleanly to a copy of `data/kasir.db` (63,826 rows preserved, 13 columns, CHECK accepts `discount_partners`/`credit_cards`, CHECK rejects bogus tables, both drain indexes present) |
| US-A0-1 | `Tools/TlsSmokeTest/` builds clean on net10.0 for win-x64/osx-arm64/linux-x64. Exit-code contract verified on macOS (missing env var -> exit 1 with FAIL message). |
| US-A0-2 | `plans/gate-a0-2-litestream-decision.md` — Litestream v0.5.11 ships official Windows binaries (verified via `gh release view`), sha256 pinned, install plan documented. WalBackupService fallback not needed. |
| US-A0-outbox-migration | `Kasir.Core.Data.Schema.sql` now has 13-column sync_queue + 17-table CHECK + cloud drain index. `SyncQueueEntry` has `CloudSynced` / `CloudSyncedAt`. `SyncQueueRepository` has `GetPendingCloud` (enforces LAN-first via `AND status='synced'`) + `MarkCloudSynced`. 298/298 Kasir.Core.Tests pass, including new `SyncQueueRepositoryTests` (8 cases) and `PushServiceHmacInvarianceTests` (2 cases proving HMAC payload is unaffected by cloud bookkeeping columns). |
| US-A1-skeleton | `Kasir.CloudSync` + `Kasir.CloudSync.Tests` scaffolded; `IHost` + `BackgroundService` stub; `CloudSyncConfig` POCO; both projects in `Kasir.Avalonia.slnx`. Clean build, 2/2 skeleton smoke tests pass. |
| US-A2-product-mapper | `ProductMapper` (Reader + Dictionary variants), `DateParser` with format allow-list + warning signal, `PostgresSink` with `INSERT ... ON CONFLICT DO UPDATE` (internal `BuildUpsertSql` exposed via `InternalsVisibleTo` for deterministic tests), `OutboxReader` that routes only products + preserves non-products rows for future mappers, `Sql/products.sql` with `pg_trgm` generated column + GIN index. 15/15 CloudSync.Tests pass. |
| US-A3-e2e | Integration test `Kasir.CloudSync.Tests/E2E/PhaseAEndToEndTests.cs` marked `[Explicit]`. Two scenarios: INSERT->Supabase, UPDATE->Supabase (ON CONFLICT path). Runs against any Postgres via `KASIR_CLOUDSYNC_TEST_PG` env var. Documented Docker + Supabase invocation in this file (below). |

## Test totals

- `Kasir.Core.Tests`     : 298 passed, 0 failed
- `Kasir.CloudSync.Tests`: 17 passed (15 unit + 2 skeleton smoke), 0 failed
- E2E tests: skipped by default (`[Explicit]`); ready to run against real Postgres

## What remains before Phase A is "production closed"

1. **Gate A0.1 run on Windows 10 target hardware.** `Tools/TlsSmokeTest`
   must be copied to Register 01 (or the chosen gateway machine), run, and
   a PASS recorded in `plans/phase-a-preflight-results.md`. Cannot run from
   macOS.
2. **Live Supabase provisioning.** Project in Singapore region, service-role
   key captured, connection string stored via Windows DPAPI on the gateway.
3. **E2E run against Supabase.**
   ```bash
   export KASIR_CLOUDSYNC_TEST_PG="Host=db.PROJECT.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=...;SslMode=Require"
   cd kasir-pos
   dotnet test Kasir.CloudSync.Tests --filter "FullyQualifiedName~PhaseAEndToEndTests"
   ```
4. **Gate A0.3 applied to the real registers.** This is the `sync_queue`
   recreation. The SQL is proven on a data-copy; apply during an end-of-day
   downtime window to all 4 DBs (3 registers + hub) in the same deploy.
5. **Litestream install on gateway.** Per `plans/gate-a0-2-litestream-decision.md`
   step-by-step PowerShell recipe.
6. **1 week stability observation** with the gateway running the CloudSync
   skeleton + Litestream against production traffic, before any Phase B
   work starts.

## E2E test — local Docker recipe

For fast iteration on macOS/Linux without paying the Supabase free-tier
connection budget:

```bash
docker run --rm -d -p 5432:5432 \
    -e POSTGRES_PASSWORD=test \
    --name kasir-cloudsync-test \
    postgres:15

export KASIR_CLOUDSYNC_TEST_PG="Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=test"

cd kasir-pos
dotnet test Kasir.CloudSync.Tests --filter "FullyQualifiedName~PhaseAEndToEndTests"

# cleanup
docker stop kasir-cloudsync-test
```

The container lifetime is bounded by the test run; data is discarded on
`--rm`. Suitable for CI if the CI runner has Docker; otherwise keep the
test `[Explicit]` so it stays opt-in.

## Deferred to later PRD iterations

Per `prd.json -> deferred_phases`:

- Phase B — core mappers (sales, sale_lines, stock, vendors, departments) +
  source generator + schema-diff CI
- Phase C — remaining ~50 mappers + initial bulk load + FK constraint enable
- Phase D — observability (localhost:5080 HttpListener health), Litestream
  restore drill, remote health via `_sync_health` table
- Phase E — `pg_trgm` search confirmation on real Supabase, free-tier
  capacity monitoring, ops runbook
- Track B — `Kasir.WebApi` ASP.NET Core service on Register 01

Append these as new stories to `/Users/anan/Code/kasir/.omc/prd.json` and
resume ralph to drive them.
