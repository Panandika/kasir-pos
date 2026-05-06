# Gate A0.1 — TLS Smoke Test (Windows 10 hardware)

Small self-contained .NET 10 console app that verifies `Npgsql` can negotiate
TLS 1.2/1.3 with Supabase Postgres from the target Windows 10 box.
**First task of Phase A.** No other Phase A work proceeds until this passes.

## Why this exists

The cloud-sync plan requires the gateway (Register 01) to open TLS connections
to Supabase (`db.<project>.supabase.co:5432`) with `SslMode=Require`.
The earlier Windows 7 concern is obsolete (we are on Windows 10 build ≥ 1903,
which supports TLS 1.2/1.3 natively), but we still verify empirically before
committing to the architecture.

## One-time setup on the target Windows 10 machine

1. Install .NET 10 SDK (or use the self-contained publish from below).
2. Get the Supabase connection string from the project dashboard:
   `Settings -> Database -> Connection string -> .NET`.
   Use the **session pooler** on port `6543` if free-tier direct-connection
   limits are a concern; otherwise direct `5432` is fine for a one-shot test.
3. Set the env var so the test can pick it up:
   ```cmd
   setx SUPABASE_CONN_STRING "Host=db.PROJECT.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=...;SslMode=Require;Trust Server Certificate=false"
   ```
   Then open a **new** `cmd` window so `setx` takes effect.

## Running the test

From macOS/Linux dev box (build only, actual run requires the env var on the
target box):
```bash
cd kasir-pos/Tools/TlsSmokeTest
dotnet build
```

Self-contained publish for deployment to the Windows 10 box:
```bash
cd kasir-pos/Tools/TlsSmokeTest
dotnet publish -c Release -r win-x64 --self-contained true -o ./publish
```
Copy `./publish` to the target. Run:
```cmd
cd publish
TlsSmokeTest.exe
```

## Expected output on PASS

```
Gate A0.1 TLS smoke test — 2026-04-25T08:30:00Z
OS: Microsoft Windows NT 10.0.19045.0 (Win32NT)
Runtime: .NET 10.0.x

Host       : db.project.supabase.co
Port       : 5432
Database   : postgres
Username   : postgres
SslMode    : Require
TrustCert  : False

Opening connection (TLS handshake happens here)...
  Opened. Server reports version 15.x.

Query 1: SELECT 1
  Result: 1
Query 2: SELECT version()
  PostgreSQL 15.x on x86_64-pc-linux-gnu, ...
Query 3: SHOW ssl  (Postgres reports TLS status of the session)
  ssl = on
Query 4: SELECT ssl_cipher, ssl_version FROM pg_stat_ssl WHERE pid = pg_backend_pid()
  TLS version : TLSv1.3
  TLS cipher  : TLS_AES_256_GCM_SHA384

GATE A0.1: PASS
```

## Failure paths

### Exit 1 — Missing / invalid env var
Re-set `SUPABASE_CONN_STRING` with `setx` and open a new cmd window.

### Exit 2 — Npgsql threw during Open or Query
Likely causes (in decreasing order of probability on Windows 10):

1. **Firewall/egress blocked.** The box cannot reach `*.supabase.co:5432`.
   Check corporate firewall, VPN, or ISP-level blocks. Not a client-TLS issue.
2. **Bad credentials.** Wrong password, project ref, or username. Re-copy
   from the Supabase dashboard.
3. **Supabase project paused.** Free-tier projects pause after 1 week of
   inactivity. Un-pause in the Supabase dashboard.
4. **TLS protocol mismatch (rare on Windows 10).** If the error mentions
   `protocol version` or `SSL3 alert`, try:
   - Ensure Windows 10 build ≥ 1903 (`winver`). Apply latest Windows Updates.
   - Retry with `SslMode=Prefer` — captures whatever protocol the server agrees
     to. Log the negotiated version from the Query 4 output and escalate.
5. **Server certificate validation failure.** If the error mentions
   `remote certificate is invalid`, confirm the Supabase certificate chain
   validates on the box (should be fine on patched Win10). As a temporary
   diagnostic — **not** a production fix — try
   `Trust Server Certificate=true` and document the certificate issue.

### Exit 3 — Query returned unexpected result
Almost never happens. Means Postgres answered but with garbage — likely a
proxy intercepting the connection. Investigate network path.

## Documenting the result

When the test PASSes on target hardware, record in
`plans/phase-a-preflight-results.md`:

- Date/time of test
- Target machine (hostname + Windows 10 build)
- `SslMode` used
- Negotiated TLS version + cipher (from Query 4)
- Any fallback paths attempted

This closes Gate A0.1 and unblocks Phase A code work.
