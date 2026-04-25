# Litestream Restore Drill — Phase D US-D3

A backup that has never been restored is not a backup. This drill exercises
the full restore path monthly so a real incident is the second time we run
it, not the first.

## Cadence

Run on the **first business day of every month**. Calendar reminder is
preferred over relying on someone to remember.

## Procedure

```powershell
cd C:\path\to\kasir-pos\Tools\LitestreamDrill
powershell -ExecutionPolicy Bypass -File .\restore-and-verify.ps1
```

The script does:

1. **`litestream restore`** — pulls the latest WAL stream from R2 to a
   temp file (`%TEMP%\kasir-restore\kasir-restored.db`). Source is
   untouched.
2. **`PRAGMA integrity_check`** — runs the SQLite integrity check on the
   restored copy. Anything other than `ok` is a hard fail.
3. **Row-count parity** — compares row counts on the critical tables
   (products, sales, sale_items, stock_movements, sync_queue) between
   source and restored. Small drift on `sync_queue` is acceptable (WAL
   replication has seconds-level lag); >1% drift on data tables fails
   the drill.
4. **File-size sanity** — logs source vs restored size; large divergence
   is a flag for operator attention even if other checks pass.

Exit 0 = drill passed. Exit 1 = drill failed (see logs). Exit 2 =
tooling broken (Litestream not installed; `sqlite3` not on PATH).

## Failure-mode catalog

| Symptom | Most likely cause | Recovery |
|---|---|---|
| `litestream restore` fails with `bucket not found` | R2 credentials rotated, bucket renamed, or service down | Verify R2 IAM token in `litestream.yml`. Check Cloudflare status. |
| `PRAGMA integrity_check` reports corruption | WAL replication captured a torn write | Restore from a previous generation: `litestream restore -timestamp <iso8601>`. Investigate why current WAL is bad. |
| Row-count mismatch >1% on data tables | Litestream service was down for an extended period; WAL contains fewer transactions than the source | Check `litestream.exe` Windows service status on gateway. Re-enable + wait for catch-up. |
| `litestream.exe` missing | Service uninstalled or path changed | Reinstall per `plans/gate-a0-2-litestream-decision.md`. |
| Restored DB is empty (0 rows everywhere) | Litestream never replicated; the configured DB path may be wrong | Verify `litestream.yml` `dbs:` entry points at the actual `kasir.db`. |
| Drill passes but takes >10 minutes for a 55MB DB | Network throttling or R2 cold-storage tier | Acceptable up to 30 minutes; if longer, investigate R2 pricing tier. |

## Escalation

Two consecutive drill failures = treat as **production incident**:

1. Document the failure in the incident log.
2. Stop the steady-state CloudSync service to avoid additional cloud divergence.
3. Open a tracking issue against this branch's milestone.
4. Until restored, treat the **local SQLite as the only authoritative
   data source.** Do not rely on the Postgres mirror for any decisions.

## Why this drill is mandatory and why we run it monthly

A monthly drill catches:
- R2 credential expiry
- Litestream service crashes the gateway didn't notice
- Slow WAL replication that doesn't fail loudly
- Surprises after Windows / .NET runtime updates on the gateway

Less than monthly = enough time accumulates that the first failure is a
real incident with no operator muscle memory. More than monthly = budget
overhead for marginal additional confidence.
