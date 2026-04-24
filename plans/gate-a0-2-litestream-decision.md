# Gate A0.2 — Litestream Decision

**Status:** RESOLVED — outcome (a), official Windows binary exists.
**Decided:** 2026-04-25
**Supersedes:** Architect iteration-2 concern that "no official Windows build exists as of 2025."

---

## Decision

**Use the official Litestream Windows binary.** No custom fallback needed.

- Version pinned for Phase A: **`v0.5.11`** (published 2026-04-08).
- Asset: `litestream-0.5.11-windows-x86_64.zip`.
- SHA-256 (from GitHub release `checksums.txt`):
  `9116e8605d4b479e15044ccbccf2aa756be7a8a64b9237f67a074ec1742444a3`
- Download URL:
  `https://github.com/benbjohnson/litestream/releases/download/v0.5.11/litestream-0.5.11-windows-x86_64.zip`

## Evidence

Ran `gh release view --repo benbjohnson/litestream` on 2026-04-25. The v0.5.11
release asset list includes both `windows-x86_64.zip` and `windows-arm64.zip`
alongside the Linux/macOS builds. Download count on the x86_64 asset at time
of check: 49. Project is actively maintained — v0.5.11 shipped in April 2026,
with v0.5.10 and v0.5.9 earlier in 2026.

Cross-check commands (re-run before install to confirm the pin is still
current):

```bash
gh release list --repo benbjohnson/litestream --limit 5
gh release view v0.5.11 --repo benbjohnson/litestream \
    --json assets --jq '.assets[] | select(.name | test("windows-x86_64"))'
```

## Installation plan (for the Phase A Windows 10 gateway)

1. Download + SHA-256 verify the pinned release:
   ```powershell
   $url = "https://github.com/benbjohnson/litestream/releases/download/v0.5.11/litestream-0.5.11-windows-x86_64.zip"
   $zip = "$env:TEMP\litestream-0.5.11-windows-x86_64.zip"
   Invoke-WebRequest -Uri $url -OutFile $zip
   (Get-FileHash $zip -Algorithm SHA256).Hash
   # Expect: 9116E8605D4B479E15044CCBCCF2AA756BE7A8A64B9237F67A074EC1742444A3
   ```
2. Extract to `C:\Program Files\Litestream\` (creating the directory as admin).
3. Create the service account (local user `svc_litestream`, no interactive logon).
4. Create `C:\ProgramData\Litestream\litestream.yml` — Cloudflare R2 replica,
   scoped IAM credentials via `${env:R2_ACCESS_KEY_ID}` /
   `${env:R2_SECRET_ACCESS_KEY}` (permissions set so only the service account
   can read the file).
5. Register as a Windows service via NSSM or `sc create` (NSSM is recommended
   because Litestream is a standard console app, not a native Windows service;
   Litestream docs reference this pattern for Windows deployments).
6. Verify with a restore drill (documented in Phase D, not Phase A).

The Phase D restore drill is what actually closes this gate operationally —
installing the binary is not sufficient; we must prove the restore works end
to end on production data. Phase D is where that happens. Phase A only needs
the binary present + config file scaffolded.

## WAL mode compatibility note

Litestream requires the source SQLite database to be in WAL mode. Our
`kasir.db` is already in WAL (per `Kasir.Core.Data.DbConnection`). Verified:
```bash
sqlite3 data/kasir.db "PRAGMA journal_mode;"
# expect: wal
```

## Alternative considered but rejected

Custom `WalBackupService.cs` using `sqlite3_backup` API on a 5-minute schedule
with AWSSDK.S3 to Cloudflare R2. **Rejected** because:

1. Litestream ships an official Windows binary, eliminating the premise that
   triggered the fallback design.
2. Litestream streams WAL at second-level granularity — a 5-minute periodic
   `sqlite3_backup` has an RPO that is 60x worse for the same DR threat.
3. Maintaining a bespoke backup service duplicates code that the upstream
   project already handles robustly, with CI, releases, and community support.

If a future version of Litestream drops Windows support, or if a specific
Windows-only edge case breaks in production, revisit this decision and
resurrect the `WalBackupService` design. Not today.

## Plan updates required (non-blocking for this story)

The master plan at `plans/cloud-sync-and-local-api.md` still describes the
WalBackupService fallback in:

- Section 1, Pre-mortem Scenario 4 ("Litestream Windows binary does not exist")
- Phase A, Gate A0.2 (decision tree)

These sections are not **wrong** — they correctly describe what we would do
if the binary disappeared — but they are now stale relative to current reality.
A follow-up doc-only commit should add a note at both locations pointing to
this decision file. Low priority; does not block Phase A work.
