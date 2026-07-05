# Bantuan Help Assistant — Production Deploy Plan

**Date:** 2026-05-10
**Revision:** 3 (consensus loop iteration 3 — Critic ACCEPT-WITH-RESERVATIONS fixes applied)
**PR:** #32 (`feat/bantuan-help-assistant`)
**Mode:** DELIBERATE (migrations + auth + production cutover = high risk)
**Repos:** kasir (POS) + sinar-makmur-dashboard (Supabase migrations)

---

> ## ⚠️ Deployment outcome (2026-07-05 retrospective) — reality diverged from this plan
>
> Deployed and verified live. Where reality differs, **reality wins**:
>
> 1. **Migration slots moved**: planogram claimed 0031/0032 first, so Bantuan
>    landed as `0033_help_schema.sql` + `0034_help_faq_search_rpc.sql`
>    (+ matching `_down/`). All references to 0031/0032 below are stale.
> 2. **Edge functions live in the dashboard repo**, not kasir-pos: deployed
>    from `sinar-makmur-dashboard/supabase/functions/{help-ask, help-report,
>    help-faq-ingest}` — all ACTIVE v2 since 2026-05-17. Decision D1 below
>    (Option A, keep in kasir-pos) was reversed in practice.
> 3. Verified live 2026-07-05: migrations 0033/0034 applied on project
>    `mnatezzsysmadvrosnad` (`supabase migration list` in sync through 0043).
>
> Kept for the decision history; do not follow the steps below verbatim.

---

## Migration Number Coordination

> **Bantuan uses migration slots 0031 and 0032.**
>
> Planogram migrations occupy slots 0027-0030, merged to `origin/main` via PR #40 (commit `7ddeead` — "feat(planogram): Phase 1 of Drop 1 — schema, atoms, test infra (#40)"). Verified: `git ls-tree origin/main supabase/migrations/` confirms 0027-0030 exist on main.
>
> - `0031_help_schema.sql`
> - `0032_help_faq_search_rpc.sql`
> - `_down/0031_help_schema.down.sql`
> - `_down/0032_help_faq_search_rpc.down.sql`
>
> **Rules:**
>
> 1. **Pre-branch checkpoint:** Before creating the Bantuan branch in the dashboard repo, run `git fetch origin main` and verify planogram 0027-0030 are present and no other work has claimed 0031+.
> 2. **Pre-merge rule (CRITICAL):** Renaming is only safe BEFORE the Bantuan PR merges to `main`. Since `supabase db push` runs ONLY AFTER merge (see Phase 2), filenames are always final before any DB write. This eliminates migration drift entirely — no manual `UPDATE supabase_migrations.schema_migrations` is ever needed.
> 3. **Branch hygiene:** The Bantuan branch in the dashboard repo MUST be created from clean `origin/main` (which now includes planogram 0027-0030). Use `git checkout -b feat/bantuan-migrations origin/main`.

---

## Principles

1. **Single source of truth for schema** — All Supabase migrations live in `sinar-makmur-dashboard/supabase/migrations/`. No raw SQL in kasir-pos except Edge Functions.
2. **Graceful degradation is the default** — Every online feature (vector search, ticket sync) must degrade silently to offline (FTS5, local queue). A network failure must never block a sale.
3. **Rollback before rollout** — Every forward step has a documented reverse step. Down migrations, git SHA rollback for Edge Functions, config flags.
4. **One thing at a time** — Deploy in phases with verification between each. Never batch schema + functions + auth + client wiring into a single push.
5. **Win7 reality check** — Every change ships only after physical register verification. TLS 1.2 on .NET 10 self-contained is fine, but test on actual hardware.

## Decision Drivers

1. **Migration drift risk** — Two repos touching the same Supabase project. Schema must flow through one migration tree only. Compounded by planogram occupying adjacent slots.
2. **Machine auth bootstrapping** — Registers need refresh tokens stored in Windows Credential Manager. First-time setup is manual per register (3 registers).
3. **Deploy ordering** — Schema must exist before Edge Functions can reference tables. Functions must exist before client can call them. Auth must exist before functions accept requests.

---

## Contested Decisions

### D1: Where do Edge Functions live?

| Option | Pros | Cons |
|--------|------|------|
| **A. Keep in kasir-pos** (chosen) | Feature locality (C# client + TS function in same PR). Dev sees both sides. No cross-repo PR dependency for function changes. | `supabase functions deploy` runs from kasir-pos, not dashboard. Two deploy contexts for one Supabase project. |
| B. Move to sinar-makmur-dashboard | Single deploy context. Migration + function in one repo. | Function changes require dashboard PR even when only POS behavior changed. Dashboard is a React PWA — Deno functions are foreign there too. |

**Why A:** Edge Functions are called exclusively by the POS client. The dashboard never calls them. Feature locality > deploy uniformity. Version via git SHA + CHANGELOG in function dir.

### D2: Machine auth approach

| Option | Pros | Cons |
|--------|------|------|
| **A. Supabase Auth machine user** (chosen) | Already coded in `_shared/auth.ts` and `HttpHelpReportClient.cs`. JWT with `app_metadata.store_id`. Standard Supabase auth flow. | One user per register = 3 users to create manually. Refresh token rotation is implicit (Supabase default 1 week). |
| B. Service role key on register | Simpler — no user creation, no token refresh. | Grants full DB access from a register. One leaked key = full Supabase admin. Violates least privilege. |

**Why A:** Security. A register compromise with option B exposes all store data. Machine users scope access via Edge Functions only.

### D3: Deploy ordering

| Option | Pros | Cons |
|--------|------|------|
| **A. Schema -> Functions -> Auth -> Client wiring** (chosen) | Each step verifiable before next. Functions can be tested with curl before any C# changes. | 4 phases = slower. |
| B. Ship everything in one push | Fast. One PR merge, one deploy script. | If schema fails, functions are broken. If auth fails, can't test functions. Rollback is all-or-nothing. |

**Why A:** Solo dev, production store, no staging environment. Sequential with verification is the only safe path.

---

## Pre-Mortem (4 failure scenarios)

### PM1: pgvector extension not enabled on Supabase project
**Trigger:** `CREATE EXTENSION IF NOT EXISTS vector` fails because the Supabase project plan doesn't include pgvector, or it requires manual enablement via dashboard.
**Impact:** Migration 0031 (`help_schema`) fails. All downstream steps blocked.
**Mitigation:** Before running migration, verify via Supabase Dashboard > Database > Extensions that `vector` is available. If not, enable it via dashboard UI first. The SQL is idempotent so re-run is safe.

### PM2: Windows Credential Manager API unavailable on Win7 SP1
**Trigger:** .NET 10 self-contained app calls `CredRead`/`CredWrite` but the P/Invoke signature or DPAPI behavior differs on Win7 SP1 vs Win10.
**Impact:** Refresh token cannot be stored. Machine auth fails on every restart. Tickets queue forever.
**Mitigation:** Test credential storage on Win7 register BEFORE wiring auth into the main app. Fallback: store encrypted refresh token in `%APPDATA%/Kasir/auth.dat` with DPAPI `ProtectedData.Protect()` (user-scope). Both approaches use Windows user-scope encryption.

### PM3: Edge Function cold start exceeds 1-second timeout
**Trigger:** `HttpHelpAskClient.Timeout` is 1 second. Supabase Edge Functions on free plan cold-start in ~800ms. OpenAI embedding call adds ~300ms. First request after idle always times out.
**Impact:** TANYA mode always returns empty on first query after idle. User thinks AI is broken.
**Mitigation:** Increase `HttpHelpAskClient.Timeout` to 5 seconds for v1. The UI already shows a spinner. Alternatively, add a warm-up ping on `HelpSyncService` start (fire-and-forget GET to function URL).

### PM4: Another PR claims migration slots 0031+ while Bantuan PR is open
**Trigger:** An unexpected PR merges to `sinar-makmur-dashboard` main between now and Bantuan merge, claiming slots 0031+.
**Impact:** If Bantuan merges without renumbering, two different migrations share the same slot numbers. `supabase db push` will skip or error.
**Mitigation:** This is SIMPLE because `supabase db push` runs ONLY AFTER the Bantuan PR merges (Phase 2). The DB has never seen Bantuan filenames, so renumbering is a pure git operation:
1. Run Phase 1 (Pre-Merge Renumber Check) — `git fetch origin main`, check latest migration number.
2. If 0031+ is taken, `git mv` the Bantuan files to the next free slots (up + down).
3. Force-push the branch, wait for CI, then merge.
4. No DB recovery needed — no `UPDATE supabase_migrations.schema_migrations`, no drift risk, because nothing was applied yet.
**Note:** Planogram (0027-0030) already merged via PR #40, so that specific collision is resolved. This pre-mortem covers any future unexpected merge.

---

## Execution Plan

### Phase 0: Migrate SQL to Dashboard Repo + Open PR

**What:** Move raw SQL files from kasir-pos to dashboard migration tree. Write down migrations. Push branch and open PR. **Stop here — do NOT apply schema or run `supabase db push`.**

**Pre-flight checkpoint:**
- [ ] `git fetch origin main` in `sinar-makmur-dashboard`
- [ ] Verify planogram migrations 0027-0030 are present on `origin/main` (PR #40 merged) and no other work has claimed 0031+
- [ ] Create Bantuan branch from clean `origin/main`: `git checkout -b feat/bantuan-migrations origin/main`
- [ ] Confirm planogram files ARE included in the branch base (they are on `origin/main` now)

**Steps:**
1. Copy `kasir-pos/db/supabase/001_help_schema.sql` content into `sinar-makmur-dashboard/supabase/migrations/0031_help_schema.sql`
   - Wrap in transaction: `BEGIN; ... COMMIT;`
   - Keep `IF NOT EXISTS` / `CREATE OR REPLACE` (idempotent)
2. Copy `kasir-pos/db/supabase/002_help_faq_search_rpc.sql` content into `sinar-makmur-dashboard/supabase/migrations/0032_help_faq_search_rpc.sql`
3. Create `sinar-makmur-dashboard/supabase/migrations/_down/0031_help_schema.down.sql`:
   ```sql
   DROP FUNCTION IF EXISTS public.help_rate_limits_trim();
   DROP TABLE IF EXISTS public.help_rate_limits;
   DROP TABLE IF EXISTS public.help_faq;
   DROP TABLE IF EXISTS public.help_tickets;
   ```
4. Create `sinar-makmur-dashboard/supabase/migrations/_down/0032_help_faq_search_rpc.down.sql`:
   ```sql
   DROP FUNCTION IF EXISTS public.help_faq_search(vector, int);
   ```
5. Delete `kasir-pos/db/supabase/001_help_schema.sql` and `kasir-pos/db/supabase/002_help_faq_search_rpc.sql`
6. Update `kasir-pos/db/supabase/README.md` — remove "Apply schema" psql commands, add note that schema lives in dashboard migrations
7. Commit, push branch, open PR on `sinar-makmur-dashboard`

**Milestone: PR is open.** This phase ends here. No `psql`, no `supabase db push`, no schema applied to production.

**Acceptance criteria:**
- [ ] `0031_help_schema.sql` and `0032_help_faq_search_rpc.sql` exist in dashboard migrations dir
- [ ] Down migrations exist (`_down/0031_help_schema.down.sql`, `_down/0032_help_faq_search_rpc.down.sql`) and are inverse of up
- [ ] Raw SQL files removed from kasir-pos `db/supabase/`
- [ ] README updated to point to dashboard repo for schema
- [ ] Dashboard CI passes (if any migration lint exists)
- [ ] Bantuan branch is based on `origin/main` (which includes planogram 0027-0030)
- [ ] PR is open and passing CI

**Rollback:** Close the PR without merging. Schema files still in kasir-pos git history.

---

### Phase 1: Pre-Merge Renumber Check (Safety Gate)

**What:** Before merging the Bantuan PR, verify migration slot numbers are still free on `origin/main`. Since planogram (0027-0030) is already merged via PR #40, and Bantuan uses 0031/0032, the expected outcome is: no rename needed.

**When to run:** Immediately before Phase 2 (merge + apply). Can be repeated if merge is delayed.

**Checklist:**
- [ ] `git fetch origin main` in `sinar-makmur-dashboard`
- [ ] Check latest committed migration number on `origin/main`:
  ```bash
  ls -1 supabase/migrations/ | grep -E '^[0-9]{4}' | sort | tail -1
  ```
- [ ] **Expected: latest is 0030 (planogram).** Bantuan's 0031/0032 are clear. Proceed to Phase 2.
- [ ] **If another PR has landed claiming 0031+ (unexpected):**
  1. Rebase Bantuan branch onto `origin/main`
  2. Renumber via `git mv` to the next free slots
  3. Commit the rename, force-push the branch
  4. Wait for CI to pass on the updated branch
  5. Proceed to Phase 2

**Why this is safe:** No `supabase db push` has run yet. The DB has never seen these filenames. Renaming is a pure git operation with zero DB state to reconcile.

**Acceptance criteria:**
- [ ] Bantuan migration filenames do not collide with any migration on `origin/main`
- [ ] Down-migration filenames match their corresponding up-migration filenames
- [ ] CI passes after any renumbering
- [ ] No `supabase db push` has been run against any Bantuan migration filename

**Rollback:** Not applicable — this phase only renames files in a branch. If something goes wrong, reset the branch and redo.

---

### Phase 2: Merge Bantuan PR + Apply Schema to Production Supabase

**What:** Merge the Bantuan PR to `main`, THEN run migrations against live Supabase project. The merge is the gate — no DB writes until filenames are final in committed history.

**Gate: Bantuan PR merged to `main`.** Do not proceed to `supabase db push` until the merge commit exists on `origin/main`.

**Steps:**
1. **Merge the Bantuan PR** (squash-merge on GitHub)
2. `git pull origin main` to confirm merge landed
3. Verify pgvector extension is enabled: Supabase Dashboard > Database > Extensions > `vector`
4. Verify pgcrypto extension is enabled (likely already — used by `gen_random_uuid()`)
5. Take note of current table count for rollback verification
6. Apply schema from merged `main`:
   ```bash
   cd sinar-makmur-dashboard
   supabase db push --linked
   ```
   Or manually via psql if `supabase db push` doesn't pick up new files:
   ```bash
   psql "$DIRECT_CONNECTION_STRING" -f supabase/migrations/0031_help_schema.sql
   psql "$DIRECT_CONNECTION_STRING" -f supabase/migrations/0032_help_faq_search_rpc.sql
   ```
7. Verify tables exist:
   ```sql
   SELECT table_name FROM information_schema.tables
   WHERE table_schema = 'public' AND table_name LIKE 'help_%';
   -- Expected: help_tickets, help_faq, help_rate_limits
   ```
8. Verify RPC exists:
   ```sql
   SELECT routine_name FROM information_schema.routines
   WHERE routine_schema = 'public' AND routine_name = 'help_faq_search';
   ```
9. Verify HNSW index:
   ```sql
   SELECT indexname FROM pg_indexes WHERE tablename = 'help_faq' AND indexname LIKE '%embedding%';
   ```
10. Wire `help_rate_limits_trim()` — the schema defines this function but nothing calls it yet.
    **Decision: Probabilistic invocation from `help-report` Edge Function (1/100 random call).**
    Add to `help-report/index.ts` after successful ticket insert:
    ```ts
    if (Math.random() < 0.01) {
      await supabase.rpc('help_rate_limits_trim').catch(() => {});  // fire-and-forget, never fail the request
    }
    ```
    **Why this over alternatives:**
    - pg_cron: Requires extension enablement, Supabase free plan may not support it, adds operational dependency.
    - Scheduled Edge Function: Requires Supabase scheduled functions setup (cron syntax in config), adds deploy complexity for a trivial cleanup.
    - Probabilistic: Zero dependencies, zero config, triggers naturally with usage. At 5 tickets/day, trims ~once every 20 days — sufficient since records expire after 1 hour anyway.
    Without this, `help_rate_limits` grows unbounded and the observability test ("trim function works") cannot pass.

11. Verify migration filenames match DB record:
    ```sql
    SELECT name FROM supabase_migrations.schema_migrations ORDER BY name DESC LIMIT 5;
    ```
    Filenames in this query MUST match the filenames committed on `origin/main`. Since we applied after merge, this is guaranteed.

**Acceptance criteria:**
- [ ] Bantuan PR is merged to `main` (merge commit exists)
- [ ] `supabase db push` ran from merged `main` (not from a branch)
- [ ] 3 tables created (`help_tickets`, `help_faq`, `help_rate_limits`)
- [ ] 2 functions exist (`help_faq_search`, `help_rate_limits_trim`)
- [ ] HNSW index on `help_faq.embedding`
- [ ] Existing dashboard data unaffected (spot-check a known table)
- [ ] `supabase_migrations.schema_migrations` filenames match committed filenames exactly (zero drift)

**Rollback:**
- **Pre-merge (PR still open):** Close the PR without merging. No DB state to clean up.
- **Post-merge, post-apply (schema in DB):**
  ```bash
  psql "$DIRECT_CONNECTION_STRING" -f supabase/migrations/_down/0032_help_faq_search_rpc.down.sql
  psql "$DIRECT_CONNECTION_STRING" -f supabase/migrations/_down/0031_help_schema.down.sql
  ```
  Down migrations revert the schema. Revert the merge commit on `main` if needed.

---

### Phase 3: Deploy Edge Functions

**What:** Deploy 3 Edge Functions + set secrets from kasir-pos.

**Steps:**
1. Link project (if not already):
   ```bash
   cd /Users/anan/Code/kasir-worktrees/bantuan-help-assistant/db/supabase
   supabase link --project-ref mnatezzsysmadvrosnad
   ```
2. Deploy functions:
   ```bash
   supabase functions deploy help-report --project-ref mnatezzsysmadvrosnad
   supabase functions deploy help-ask --project-ref mnatezzsysmadvrosnad
   supabase functions deploy help-faq-ingest --project-ref mnatezzsysmadvrosnad
   ```
3. Set secrets (one-time):
   ```bash
   supabase secrets set OPENAI_API_KEY=sk-... --project-ref mnatezzsysmadvrosnad
   ```
4. Record deploy git SHA for rollback and **commit + push** it:
   ```bash
   git -C /Users/anan/Code/kasir-worktrees/bantuan-help-assistant rev-parse HEAD > db/supabase/functions/DEPLOY_SHA.txt
   cd /Users/anan/Code/kasir-worktrees/bantuan-help-assistant
   git add db/supabase/functions/DEPLOY_SHA.txt
   git commit -m "chore: record Edge Function deploy SHA"
   git push
   ```
   **Note:** DEPLOY_SHA.txt is the only audit trail for which commit is live on Supabase. If not committed and pushed, it provides no value — a local-only file is lost on any clean checkout.
5. Smoke test with curl (no auth yet — expect 401):
   ```bash
   curl -s -o /dev/null -w "%{http_code}" \
     -X POST "https://mnatezzsysmadvrosnad.supabase.co/functions/v1/help-report" \
     -H "content-type: application/json" \
     -d '{}'
   # Expected: 401 (no auth header)
   ```

**Acceptance criteria:**
- [ ] All 3 functions deploy without error
- [ ] OPENAI_API_KEY secret set
- [ ] Monthly budget alert set in OpenAI dashboard (suggest $20/mo for 1 store) — not a code change, but operational requirement to prevent surprise bills
- [ ] Unauthenticated POST returns 401 (not 404 or 500)
- [ ] DEPLOY_SHA.txt committed and pushed to kasir-pos repo

**Rollback:**
```bash
supabase functions delete help-report help-ask help-faq-ingest --project-ref mnatezzsysmadvrosnad
```

---

### Phase 4: Machine Auth Setup (per register)

**What:** Create Supabase Auth machine users, store refresh tokens on registers.

**Steps:**
1. Create machine users (run once from dev workstation):
   ```ts
   // Node/Deno script using @supabase/supabase-js
   import { createClient } from "@supabase/supabase-js";
   const sb = createClient(SUPABASE_URL, SERVICE_ROLE_KEY);

   for (const reg of ["01", "02", "03"]) {
     const { data, error } = await sb.auth.admin.createUser({
       email: `register-${reg}@sinar-makmur.local`,
       password: crypto.randomUUID(), // save these!
       email_confirm: true,
       app_metadata: { store_id: "sinar-makmur", register_id: reg },
     });
     console.log(reg, data?.user?.id, error);
   }
   ```
   Save credentials securely (password manager, NOT in repo).

2. Test login for register-01:
   ```bash
   curl -s -X POST "https://mnatezzsysmadvrosnad.supabase.co/auth/v1/token?grant_type=password" \
     -H "apikey: $ANON_KEY" \
     -H "content-type: application/json" \
     -d '{"email":"register-01@sinar-makmur.local","password":"<saved-pw>"}'
   ```
   Expect: `{ "access_token": "...", "refresh_token": "..." }`

3. Test authenticated Edge Function call:
   ```bash
   curl -s -X POST "https://mnatezzsysmadvrosnad.supabase.co/functions/v1/help-report" \
     -H "apikey: $ANON_KEY" \
     -H "authorization: Bearer $ACCESS_TOKEN" \
     -H "content-type: application/json" \
     -d '{
       "ticket_no": "TKT-SM-01-260510-test1",
       "category": "hardware",
       "body": "test ticket from deploy plan",
       "attachments": {"version":"2.4.1"}
     }'
   ```
   Expect: 200 with ticket data.

4. Verify test ticket in DB:
   ```sql
   SELECT ticket_no, store_id, register_id, status FROM public.help_tickets
   WHERE ticket_no = 'TKT-SM-01-260510-test1';
   ```

5. Clean up test ticket:
   ```sql
   DELETE FROM public.help_tickets WHERE ticket_no LIKE '%-test%';
   DELETE FROM public.help_rate_limits;
   ```

**Acceptance criteria:**
- [ ] 3 machine users created with correct `app_metadata`
- [ ] Login returns valid JWT for each register
- [ ] Authenticated help-report POST returns 200
- [ ] Ticket appears in DB with correct `store_id` from JWT (not body)
- [ ] Credentials saved in password manager

**Rollback:** Delete machine users via Supabase Dashboard > Authentication > Users.

---

### Phase 5: Client Wiring (kasir-pos PR #32)

**What:** Wire `HttpHelpAskClient` + `HttpHelpReportClient` + `HelpSyncService` into `BantuanOverlayHost` and shell boot. Increase ask timeout.

**Prerequisites:**
- `dotnet add Kasir.Core package System.Security.Cryptography.ProtectedData` — on .NET 10 this is NOT in-box (was in .NET Framework). Build will fail without it.
- HelpConfig JSON file must exist at `%APPDATA%\Kasir\help.json` (Windows) or beside the exe for dev. Schema:
  ```json
  {
    "SupabaseUrl": "https://mnatezzsysmadvrosnad.supabase.co",
    "AnonKey": "eyJ...",
    "MachineEmail": "register-01@sinar-makmur.local",
    "MachinePassword": "<from-password-manager>",
    "StoreId": "sinar-makmur",
    "RegisterId": "01"
  }
  ```
  Required fields: `SupabaseUrl`, `AnonKey`, `MachineEmail`, `MachinePassword`. `StoreId` and `RegisterId` are informational (auth uses JWT `app_metadata`). File location on registers: `%APPDATA%\Kasir\help.json`.

**Subasks:**
1. **Auth token provider** — Create `SupabaseMachineAuth` class in `Kasir.Core/Help/`:
   - Constructor takes `supabaseUrl`, `anonKey`, `email`, `password`
   - **Constructor MUST NOT throw** on missing config, corrupt `auth.dat`, or DPAPI failure — log warning and continue in unauthenticated mode
   - On first call: `signInWithPassword`, cache access + refresh tokens
   - On subsequent calls: return cached access token; if expired, use refresh token
   - **`GetAccessTokenAsync` returns empty string on failure, NOT exception** — callers already handle empty token as "offline mode"
   - **Add `SemaphoreSlim(1,1)` around refresh-token calls** — prevents concurrent refresh race between `HelpSyncService` and `HttpHelpAskClient` (both may call `GetAccessTokenAsync` simultaneously)
   - Store refresh token via `ProtectedData.Protect()` to `%APPDATA%/Kasir/help-auth.dat`
   - **Cross-platform guard (REQUIRED):** Wrap `ProtectedData.Protect/Unprotect` calls with `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)`. On non-Windows (dev macOS/Linux), skip file-based token storage entirely — store token in memory only (acceptable for dev) or use plaintext file gated behind `#if DEBUG` / environment check. Without this guard, unit tests and dev builds fail with `PlatformNotSupportedException` on macOS/Linux.
   - On app start: try loading refresh token from file, call `refreshSession`
   - Expose `Task<string> GetAccessTokenAsync(CancellationToken)`
   - **Startup race documentation:** First `HelpSyncService.TickAsync` may fire before initial `signInWithPassword` completes. Auth returns empty string, Edge Function returns 401, ticket stays in queue. With `MaxAttemptsBeforeFail=8` x 15s tick = 2min auth outage budget before permanent ticket failure. This is acceptable — tickets are not lost, just delayed.
   - **Principle assertion:** Auth singleton must never block overlay opening — offline FTS5 always works regardless of auth state

2. **Config** — Add to `Kasir.Core/Help/HelpConfig.cs`:
   ```csharp
   public class HelpConfig
   {
       public string SupabaseUrl { get; set; }
       public string AnonKey { get; set; }
       public string MachineEmail { get; set; }
       public string MachinePassword { get; set; }  // first-run only, then refresh token
   }
   ```
   Load from `kasir.json` or `help.json` beside the exe. Never hardcode keys.

3. **Fix HybridRetriever nullable field** — Change `HybridRetriever.cs:36` field type from `IHelpAskClient _remoteClient` to `IHelpAskClient? _remoteClient` to match the nullable annotation. The runtime null-check at lines 48-49 is already correct, but the field declaration must be explicitly nullable to avoid compiler warning and clarify intent.

4. **Wire BantuanOverlayHost.Open()** — Replace `null!` in `BantuanOverlayHost.cs:52` with proper nullable parameter:
   ```csharp
   var auth = SupabaseMachineAuth.Current;  // singleton, never throws
   IHelpAskClient? askClient = null;
   if (auth.IsConfigured)
   {
       askClient = new HttpHelpAskClient(httpClient, askEndpoint, anonKey, auth.GetAccessTokenAsync);
       askClient.Timeout = TimeSpan.FromSeconds(5);  // PM3 mitigation
   }
   var retriever = new HybridRetriever(faqRepo, askClient);  // already null-checks askClient
   ```
   **Note:** `HybridRetriever` already handles `null` `IHelpAskClient` — it falls back to FTS5-only. Do NOT use `null!` to satisfy the compiler; use `IHelpAskClient?` nullable parameter.

5. **Wire HelpSyncService auto-start** in `ShellWindow.axaml.cs` `OnOpened`:
   ```csharp
   var reportClient = new HttpHelpReportClient(httpClient, reportEndpoint, anonKey, auth.GetAccessTokenAsync);
   var syncService = new HelpSyncService(db, reportClient);
   _ = syncService.RunAsync(_shellCts.Token);  // fire-and-forget, cancels on shell close
   ```
   **HttpHelpReportClient 401/403 retry semantics:** Treats 401 as transient during auth bootstrap window (first ~2 minutes after app start, while `SupabaseMachineAuth` completes initial `signInWithPassword`). `MaxAttemptsBeforeFail=8` caps permanent auth misconfigurations at 2 minutes (8 x 15s tick) before tickets fail permanently. This prevents infinite retry loops on genuinely invalid credentials while tolerating the startup race.

6. **Increase HttpHelpAskClient.Timeout** from 1s to 5s (already in constructor above)

**Acceptance criteria:**
- [ ] `System.Security.Cryptography.ProtectedData` NuGet package added to Kasir.Core
- [ ] `SupabaseMachineAuth` constructor never throws (missing config / corrupt auth.dat / DPAPI failure all log-and-continue)
- [ ] `GetAccessTokenAsync` returns empty string on failure, never throws
- [ ] `SemaphoreSlim(1,1)` guards refresh-token calls (no concurrent refresh race)
- [ ] `ProtectedData.Protect/Unprotect` guarded with `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)` — non-Windows falls back to in-memory token only
- [ ] `HybridRetriever.cs:36` field type is `IHelpAskClient? _remoteClient` (explicitly nullable)
- [ ] `BantuanOverlayHost` uses `IHelpAskClient?` nullable parameter (no `null!`)
- [ ] `HelpSyncService.RunAsync` starts on shell boot, stops on shell close
- [ ] `HttpHelpReportClient` treats 401 as transient during bootstrap; MaxAttemptsBeforeFail=8 caps at 2min
- [ ] Ask timeout is 5 seconds
- [ ] Auth token persists across app restarts via encrypted file (Windows) or in-memory (non-Windows dev)
- [ ] Config loaded from `%APPDATA%\Kasir\help.json` (Windows) with documented JSON schema
- [ ] Overlay opens even when auth is unconfigured (FTS5-only mode)
- [ ] All 381+ Core tests still pass (including on macOS dev machine — no PlatformNotSupportedException)
- [ ] All 12+ Avalonia tests still pass
- [ ] `dotnet build` clean (zero warnings)

**Rollback:** Revert the commit. Bantuan still works offline (FTS5 + local ticket queue).

---

### Phase 6: FAQ Corpus Seed + Register Walkthrough

**What:** Ingest FAQ content and validate on physical Win7 registers.

**Steps:**
1. Prepare FAQ markdown corpus (product lookup help, F-key guide, common errors):
   ```bash
   # From kasir-pos, using the help-faq-ingest Edge Function
   # This requires a corpus file — create kasir-pos/docs/help-faq-corpus.md
   ```
2. Run ingest via `Tools/HelpIngest` CLI (confirmed present at `kasir-pos/Tools/HelpIngest/`):
   ```bash
   cd /path/to/kasir-pos/Tools/HelpIngest
   dotnet run -- --corpus ../../../docs/help-faq-corpus.md --endpoint "$SUPABASE_URL/functions/v1/help-faq-ingest" --anon-key "$ANON_KEY" --token "$ACCESS_TOKEN"
   ```
   If the tool's CLI interface differs, fall back to direct curl to `help-faq-ingest` Edge Function with the FAQ JSON payload.
3. Verify FAQ search works:
   ```bash
   curl -X POST "$SUPABASE_URL/functions/v1/help-ask" \
     -H "apikey: $ANON_KEY" \
     -H "authorization: Bearer $ACCESS_TOKEN" \
     -H "content-type: application/json" \
     -d '{"query": "cara cari barang"}'
   # Expect: chunks array with relevant FAQ hits
   ```

4. **Win7 Register Walkthrough** (on each of 3 registers):
   - [ ] `Ctrl+/` opens Bantuan overlay
   - [ ] Status-bar pill shows connection state (online/offline)
   - [ ] Type a question in TANYA → get FAQ results (vector + FTS5 fused)
   - [ ] `Shift+Tab` switches to LAPOR mode
   - [ ] File a test ticket → appears in Supabase `help_tickets` within 15 seconds
   - [ ] `Esc` closes overlay cleanly
   - [ ] Acrylic/glass effect renders (or degrades gracefully on Win7 without DWM composition)
   - [ ] No UI freezes during network calls
   - [ ] Kill network (unplug cable) → TANYA degrades to FTS5, LAPOR queues locally
   - [ ] Restore network → queued tickets drain within 30 seconds

**Acceptance criteria:**
- [ ] FAQ corpus ingested (>= 10 entries in `help_faq` with embeddings)
- [ ] All 6 walkthrough items pass on at least register-01
- [ ] Offline degradation verified (network kill test)

**Rollback:** FAQ data: `TRUNCATE public.help_faq;`. Register issues: revert to previous published exe.

---

### Phase 7: Kasir PR Merge + Tag

**What:** Merge kasir-pos PR #32 and tag release. (Dashboard PR was already merged in Phase 2.)

**Steps:**
1. Update kasir PR #32 description — check remaining boxes, note deferrals
2. Merge kasir PR #32
3. Tag release: `git tag v2.x.0-bantuan`
4. `dotnet publish -r win-x64 --self-contained` and deploy to registers

**Acceptance criteria:**
- [ ] Dashboard PR already merged (Phase 2 — verified)
- [ ] Kasir PR #32 merged
- [ ] Tag created
- [ ] Published exe deployed to all 3 registers
- [ ] No regressions in POS workflow (ring up a test sale)
- [ ] Migration filenames in committed history match what `supabase_migrations.schema_migrations` recorded (guaranteed by apply-after-merge ordering)

---

## Deferred Items (not in this plan)

| Item | Estimate | When |
|------|----------|------|
| G2 guided F-key watch | 2-3h | After FAQ has `guided:F8` entries |
| Voice dictation | TBD | v3 |
| RLS policies on help tables | 1h | When/if multi-store |
| Dashboard UI for ticket management | 4-6h | Next dashboard sprint |

---

## Expanded Test Plan

### Unit Tests (Kasir.Core.Tests)
- [ ] `SupabaseMachineAuth` — token caching, refresh flow, file persistence round-trip, constructor-never-throws (missing config, corrupt file, DPAPI failure), `GetAccessTokenAsync` returns empty on failure, `SemaphoreSlim` prevents concurrent refresh, cross-platform guard (ProtectedData skipped on non-Windows without exception)
- [ ] `HelpConfig` — loads from JSON file, missing file returns defaults, malformed JSON logs warning and returns defaults
- [ ] `HttpHelpAskClient` — 5s timeout honored, empty list on failure, chunk parsing
- [ ] `HttpHelpReportClient` — 409 treated as success, 429 as transient, 400 as permanent
- [ ] `HelpSyncService` — drains batch, respects max retries, cancellation token stops loop

### Integration Tests (curl against live Supabase)
- [ ] Unauthenticated request → 401
- [ ] Valid auth + valid payload → 200 + ticket in DB
- [ ] Duplicate ticket_no → 200 (idempotent)
- [ ] Rate limit: 6th ticket in 1 hour → 429
- [ ] help-ask with valid query → chunks array (after FAQ seeded)
- [ ] help-ask with empty query → 400

### E2E (Win7 register)
- [ ] Full TANYA flow: Ctrl+/ → type question → see results → Esc
- [ ] Full LAPOR flow: Ctrl+/ → Shift+Tab → fill ticket → submit → verify in Supabase
- [ ] Offline degradation: unplug network → TANYA returns FTS5 results → LAPOR queues locally
- [ ] Network restore: queued tickets drain automatically
- [ ] App restart: auth token persists, no re-login prompt

### Observability
- [ ] `HelpSyncService` logs tick count and terminal count per cycle (existing)
- [ ] `SupabaseMachineAuth` logs token refresh events (add)
- [ ] Edge Functions: check Supabase Dashboard > Edge Functions > Logs for errors after each phase
- [ ] `help_rate_limits` table stays small (trim invoked probabilistically from `help-report` Edge Function — see Phase 2 step 10)

---

## ADR: Schema Migration Ownership

**Decision:** Move Supabase schema SQL from kasir-pos `db/supabase/` to `sinar-makmur-dashboard/supabase/migrations/` as migrations 0031-0032. Keep Edge Functions in kasir-pos.

**Drivers:**
1. Single migration tree prevents drift — dashboard already owns 26 migrations for the same Supabase project
2. Edge Functions are POS-exclusive — dashboard never calls them, so feature locality favors kasir-pos
3. Solo dev needs one place to check "what's in the DB" — the migration folder in dashboard

**Alternatives considered:**
- **All in kasir-pos:** Rejected. Creates parallel migration authority. `supabase db push` from dashboard would miss help tables. Future schema changes require coordinating two migration trees.
- **All in dashboard (including Edge Functions):** Rejected. Functions are Deno/TS code tightly coupled to C# client contracts. Changing a function signature requires a kasir-pos PR anyway. Splitting across repos adds friction without benefit.
- **Shared supabase/ submodule:** Rejected. Git submodules add complexity disproportionate to a 2-repo, 1-dev setup. Merge conflicts in submodule refs are painful.

**Why chosen:** Minimizes migration drift risk (the highest driver) while preserving feature locality for the code that changes together. The split is clean: DDL in dashboard, runtime code in kasir-pos.

**Consequences:**
- Schema changes to help tables require a dashboard PR + kasir-pos PR if Edge Functions change
- Edge Function deploy is a manual `supabase functions deploy` from kasir-pos (documented in README)
- DEPLOY_SHA.txt tracks which kasir-pos commit is live on Supabase
- Migration slot numbers are definitive: 0031/0032. Planogram merged as 0027-0030 via PR #40.
- Zero migration drift guarantee: `supabase db push` runs only after Bantuan PR merges to `main`, so `supabase_migrations.schema_migrations` always records the final committed filenames. No manual DB fixups are ever needed for slot collisions.
- **RLS deferred for single-store. TRIPWIRE: enable RLS before onboarding store 2.** Edge Functions currently use `SERVICE_ROLE_KEY` which bypasses all row-level security. Cross-store data leakage is immediate without RLS. For single-store this is acceptable; for multi-store it is a hard blocker.

**Follow-ups:**
- Add a CI check or pre-deploy script that warns if `db/supabase/functions/` changed but DEPLOY_SHA.txt wasn't updated
- Consider `supabase functions deploy` in a GitHub Action on kasir-pos main branch (low priority)
- (Resolved) Planogram merged first via PR #40 — no further coordination needed
- **Align help-report response code with C# client expectations OR document 200-as-idempotent contract.** Currently: Edge Function returns 200 for duplicate `ticket_no` (idempotent upsert), but C# `HttpHelpReportClient` treats 409 as Ok (dead code path). Harmless today but confusing — either change the function to return 409 on duplicate, or remove the 409 handling in C# and document that 200 means "accepted or already exists."

---

## Rollback Summary

| Phase | Rollback action | Time to rollback |
|-------|----------------|------------------|
| 0 (migrate SQL + open PR) | Close PR without merging | 1 min |
| 1 (pre-merge renumber) | Reset branch and redo (no DB state) | 1 min |
| 2 (merge + schema apply) | Pre-merge: close PR. Post-merge: run down migrations via psql + revert merge commit | 3 min |
| 3 (Edge Functions) | `supabase functions delete` | 1 min |
| 4 (machine auth) | Delete users in Supabase Dashboard | 2 min |
| 5 (client wiring) | `git revert` kasir-pos commit; Bantuan works offline | 5 min |
| 6 (FAQ + walkthrough) | `TRUNCATE help_faq;` revert exe | 3 min |
| 7 (kasir PR merge + tag) | `git revert` merge commit on kasir-pos | 5 min |

**Full rollback (nuclear):** Revert both PRs + run all down migrations + delete Edge Functions + delete machine users. Bantuan overlay still renders but is fully offline (FTS5 + local queue, no drain). Estimated: 15 minutes.
