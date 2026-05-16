# Bantuan — Inline Glass Help Assistant (v2 — post-validation)

**Status**: Revised after 3-subagent validation. Pending re-validation before code.
**Date**: 2026-05-07
**Source design**: `claude.ai/design` handoff bundle (15 wireframe states)
**Target**: Kasir.Avalonia (12.x) / .NET 10 / C# 12
**Supabase**: project `mnatezzsysmadvrosnad` (ap-southeast-1, Singapore)

---

## 0a. Changelog vs v2 (post second validation pass)

10 minor fixes patched in. Zero new blockers. Patch IDs referenced inline.

| # | Fix | Section |
|---|-----|---------|
| 1 | `OutboxRouter` ctor refactor — inject `Dictionary<SinkKind, ISink>` | §4.5, Phase 4 |
| 2 | `TableMapping` record gains `Sink` property; existing 18 mappings default to `Generic` | §4.5, Phase 4 |
| 3 | Confirm `PushService.SyncedTables` includes `help_tickets` | Phase 4 step 12 |
| 4 | `HttpSink` treats 409 (existing ticket_no) as success — idempotent retry | §4.5 |
| 5 | Acrylic probe wrapped in 250ms timeout to prevent startup stall | §4.7 |
| 6 | Local `help_tickets.attachments_json` gains `CHECK (json_valid(...))` | §4.3 |
| 7 | `/help-report` Edge Function applies `COALESCE($client_created_at, now())` fallback | §4.4 |
| 8 | `last_error` stores only status code + short category, never raw response body | §4.5 |
| 9 | `PiiScrubber` runs deep/recursive on all string values in attachments | §4.10 |
| 10 | `/help-ask` derives `store_id` from JWT, not client body | §4.4 |

## 0b. Design-fidelity audit (post second design read)

5 callouts vs original `claude.ai/design` handoff bundle. All addressed.

| G | Item | Resolution | Section |
|---|------|-----------|---------|
| G1 | Voice state functionality | UI rendered for fidelity, state UNREACHABLE in real flow, mic disabled | §3, §4.8, Phase 8 |
| G2 | Guided state F-key watch interaction | `HelpService.WatchKeyAsync(Key, timeout)` hook into KeyboardRouter | §4.8, Phase 8 |
| G3 | Followup state prior-Q&A continuity | `LastTurn` record kept on VM, rendered as collapsed line above new question | §4.8, Phase 8 |
| G4 | Hidden state pre-trigger affordance | Status bar audit; add brand-teal `Ctrl+/ Bantuan` token to all screens (extract to `StatusBarHints` user control if 5+) | Phase 7 step 27 |
| G5 | Design-tool grid view | INTENTIONALLY NOT SHIPPED — handoff-only comparison view | §3 |
| G6 | ModePill `⇧⇥` keyboard hint glyph | Inline glyph in component | Phase 7 step 24 |

## 0. Changelog vs v1

v1 had 10 blockers across architect / database / security review. Key shifts:

| # | v1 (broken) | v2 (revised) |
|---|-------------|--------------|
| Outbox | Invented parallel `help_outbox` table | Reuse existing `sync_queue` via `PushService` + `TableMappings` |
| DB transport | Direct Npgsql/Supavisor from register | Supabase Edge Functions over HTTPS (anon key only on register) |
| RLS | Self-asserted JWT claim on anon key (spoofable) | Edge Function uses service role server-side. RLS deferred until multi-store. |
| OpenAI key | Implicit on register | Held by Edge Function only. Register never sees it. |
| Vector index | `ivfflat lists=50` | HNSW `m=16, ef_construction=64` |
| ticket_no | Collides across 3 registers | Includes register id |
| KeyboardRouter | Add DI / dispatcher | Add stateless predicate `IsCtrlSlash`, route from `ShellWindow.OnKeyDown` |
| FTS5 tokenize | `porter unicode61` (English stemmer) | `unicode61` only (Indonesian-safe) |
| Schema dates | `INTEGER` epoch | `TEXT` ISO8601 (matches all 57 existing tables) |
| Phase order | Components before host | Host shell first, fill components inside it |

## 1. Goal

Add cashier-facing in-app help assistant: AI Q&A (TANYA mode) + bug/feature report (LAPOR mode). Surface as bottom-center floating glass strip overlaying the active screen. Triggered by `Ctrl+/` from anywhere.

## 2. Locked decisions

| # | Topic | Decision |
|---|-------|---------|
| Q1 | Scope | All 15 wireframe states (00 hidden + 14 active) |
| Q2 | Backend | Supabase Edge Functions over HTTPS. Postgres direct never reached from register. |
| Q3 | AI knowledge | Hybrid: SQLite FTS5 (offline) + Supabase pgvector HNSW (online). RRF fusion. Server-side via Edge Function. |
| Q4 | Voice | Skip v1 — `voice` state rendered for design fidelity, mic disabled. |
| Q5 | Permissions | All cashiers can ask + report, no role gating. |
| Q6 | Keys | `Ctrl+/` open, `Shift+Tab` toggle, `Esc` close, digits `1`/`2`/`3` disambiguate, `Enter` submit. |
| Q7 | Position | Bottom-center floating, max-width 560px, 32px from bottom edge. |
| Q8 | Offline queue | Hold in `sync_queue` until manual clear or successful Edge Function 2xx. |
| Q9 | Auto-attach | v1: `register_id, store_id, app_version, last_invoice, last_error` (scrubbed). v2: `activity_log_30m`. |
| Q10 | Theme | Light + dark, follow existing `data-theme` system. |

## 3. Non-goals (v1)

- Voice dictation (G1: state UNREACHABLE in real navigation, code stays for v2)
- Multi-store admin UI (`store_id` in schema, only 1 store data)
- IT-side ticket triage UI (separate project)
- Realtime ack push (toast back when IT acks)
- Activity log capture (last 30min keystrokes)
- LLM answer synthesis (TANYA returns retrieved chunks raw — prompt-injection-safe)
- Design-tool grid view (G5: 2-column comparison grid, cell numbering "01 · Idle", Caveat-font annotations exist only in handoff bundle for visual comparison; runtime ships ONE overlay, never 14 cells)

## 4. Architecture

### 4.1 Trust boundary

```
[Register PC]                          [Supabase]
 .env on disk: ANON_KEY only            Edge Functions hold:
 (no Postgres password,                  - SERVICE_ROLE_KEY
  no OpenAI key)                         - OPENAI_API_KEY
                                         - Postgres credentials
       │                                  │
       │ HTTPS + anon-key                 │
       │ + machine-auth JWT               │
       └────► /functions/v1/help-report ──► public.help_tickets
              /functions/v1/help-ask    ──► public.help_faq + OpenAI
              /functions/v1/help-faq-ingest (admin)
```

`.env` cleanup: production register installs receive only `SUPABASE_URL` and `ANON_KEY`. The Postgres connection strings stay in server/CI environments. Verify `CloudSyncConfig` (line by line) does not load `CONNECTION_STRING*` on register startup paths.

### 4.2 Projects affected

```
kasir-pos/
├── Kasir.Avalonia/
│   └── Forms/Help/                    [NEW]
│       ├── BantuanGlassStrip.axaml
│       ├── BantuanGlassStrip.axaml.cs
│       ├── BantuanOverlayHost.cs       (mounts into existing OverlayHost ContentControl)
│       ├── BantuanViewModel.cs         (state machine: Mode × Phase × Connectivity)
│       ├── States/                     (15 state renderers)
│       └── Components/
│           ├── ModePill.axaml
│           ├── Chip.axaml
│           ├── Shimmer.axaml
│           ├── SyncBadge.axaml
│           └── SentToast.axaml
├── Kasir.Core/
│   └── Help/                          [NEW]
│       ├── HelpService.cs              (Ask + Report orchestration)
│       ├── KnowledgeBase/
│       │   ├── FtsIndex.cs             (SQLite FTS5)
│       │   ├── HelpAskClient.cs        (HTTPS to /help-ask Edge Function)
│       │   ├── HybridRetriever.cs      (RRF: local FTS5 + remote vector)
│       │   └── DocIngester.cs          (markdown → chunks → local FTS5; ingest to remote via admin tool)
│       ├── HelpTicketRepository.cs     (writes to local help_tickets table)
│       ├── HelpReportClient.cs         (HTTPS to /help-report; called by sync drain)
│       ├── ContextCollector.cs         (auto-attach payload, with PII scrubber)
│       └── PiiScrubber.cs              (strip PAN-like digits, amounts, PINs)
├── Kasir.CloudSync/
│   └── Generation/
│       └── TableMappings.cs            [EDIT] register help_tickets mapping
├── kasir-pos/db/
│   └── supabase/
│       ├── 001_help_schema.sql
│       └── functions/
│           ├── help-report/index.ts
│           ├── help-ask/index.ts
│           └── help-faq-ingest/index.ts
└── kasir-pos/Tools/HelpIngest/         [NEW] CLI: ingest local FAQ + push to Supabase
```

### 4.3 Data model

#### Local SQLite (`kasir.db`) — Migration_006

`Kasir.Core/Data/Migrations/Migration_006.cs` follows existing `IMigration` class pattern (not raw SQL file).

```sql
-- 4 tables. Conventions: TEXT ISO8601 dates, no INTEGER epoch.

create table help_faq (
  id              integer primary key,
  doc_path        text not null,
  anchor          text,
  title           text,
  content         text not null,
  tags            text,                                 -- comma-separated
  updated_at      text not null default (datetime('now','localtime'))
);

create virtual table help_faq_fts using fts5(
  title, content, tags,
  content=help_faq, content_rowid=id,
  tokenize='unicode61'                                  -- Indonesian-safe; no Porter
);

create trigger help_faq_ai after insert on help_faq begin
  insert into help_faq_fts(rowid, title, content, tags) values (new.id, new.title, new.content, new.tags);
end;
create trigger help_faq_ad after delete on help_faq begin
  insert into help_faq_fts(help_faq_fts, rowid, title, content, tags) values ('delete', old.id, old.title, old.content, old.tags);
end;
create trigger help_faq_au after update on help_faq begin
  insert into help_faq_fts(help_faq_fts, rowid, title, content, tags) values ('delete', old.id, old.title, old.content, old.tags);
  insert into help_faq_fts(rowid, title, content, tags) values (new.id, new.title, new.content, new.tags);
end;

create table help_tickets (
  id              integer primary key,
  ticket_no       text unique not null,                 -- TKT-SM-01-260507-0001
  store_id        text not null,
  register_id     text not null,
  cashier_id      text,
  category        text not null check (category in ('hardware','transaksi','aplikasi','saran')),
  body            text not null check (length(body) between 3 and 2000),
  attachments_json text not null check (json_valid(attachments_json)),  -- patch #6: serialization sanity
  status          text not null default 'queued' check (status in ('queued','sent','failed')),
  client_created_at text not null default (datetime('now','localtime')),
  sent_at         text,
  sync_attempts   integer not null default 0,
  last_error      text                                  -- status code + short category only, never raw body
);
create index idx_help_tickets_pending on help_tickets(status, client_created_at) where status='queued';
create index idx_help_tickets_dead on help_tickets(status, sync_attempts) where status='queued' and sync_attempts >= 5;
```

`PushService` registers `help_tickets` as a synced table → emits `sync_queue` row on insert. Drained by existing `OutboxRouter` via a new `TableMapping` whose handler is HTTP-based (see §4.5), not the default `GenericSink`.

#### Supabase (Postgres) — `db/supabase/001_help_schema.sql`

```sql
create extension if not exists vector;

create table public.help_tickets (
  id                  uuid primary key default gen_random_uuid(),
  ticket_no           text unique not null,
  store_id            text not null,
  register_id         text not null,
  cashier_id          text,
  category            text not null check (category in ('hardware','transaksi','aplikasi','saran')),
  body                text not null check (length(body) between 3 and 2000),
  attachments         jsonb not null,
  status              text not null default 'open' check (status in ('open','ack','resolved','closed')),
  client_created_at   timestamptz not null,
  server_created_at   timestamptz not null default now(),
  resolved_at         timestamptz,
  resolution_note     text
);
create index help_tickets_store_status_idx on public.help_tickets (store_id, status);
create index help_tickets_recent_idx on public.help_tickets (server_created_at desc);

-- COMMENT for downstream IT dashboard team:
comment on column public.help_tickets.body is
  'Untrusted free-text from cashier. MUST be rendered with HTML escaping in any UI. No HTML/markdown parsing.';
comment on column public.help_tickets.resolution_note is
  'Untrusted free-text from IT user. MUST be rendered with HTML escaping in any UI.';

-- RLS: deferred. Until multi-store ships, all writes go via Edge Functions
-- using SERVICE_ROLE_KEY. Function enforces store_id from machine-auth JWT.
-- When multi-store lands, enable RLS with proper Supabase Auth claim path:
--   (auth.jwt() ->> 'store_id')

create table public.help_faq (
  id              uuid primary key default gen_random_uuid(),
  doc_path        text not null,
  anchor          text,
  title           text,
  content         text not null,
  tags            text[],
  embedding       vector(1536),
  updated_at      timestamptz not null default now()
);

-- HNSW (better recall + no rebuild needed at our scale):
create index help_faq_embedding_idx on public.help_faq
  using hnsw (embedding vector_cosine_ops)
  with (m = 16, ef_construction = 64);
create index help_faq_doc_idx on public.help_faq (doc_path, anchor);
```

### 4.4 Supabase Edge Functions (Deno / TypeScript)

#### `/functions/v1/help-report`

```ts
// Validates anon-key call. Reads machine-auth JWT (Supabase Auth machine
// account, one per store) — derives store_id from JWT, NOT request body.
// Inserts via SUPABASE_SERVICE_ROLE_KEY. Rate-limits per register.
// client_created_at: server applies COALESCE($client_created_at, now()) fallback (patch #7).

POST { ticket_no, register_id, cashier_id?, category, body, attachments, client_created_at? }
auth: Bearer <register's Supabase Auth access token>
returns: 200 { id, server_created_at } | 409 (existing ticket_no, idempotent retry) | 4xx | 429
```

Rate limits enforced server-side via a small `help_rate_limits` table:
- Max 5 tickets / register / hour
- Max 50 tickets / store / day
- Reject body > 2000 chars (DB CHECK is final defence)

`ticket_no` already unique — duplicate retries (idempotent) return 200 with existing row.

#### `/functions/v1/help-ask`

```ts
// 1. Embed query via OpenAI (key held in Function env only)
// 2. pgvector cosine search top-K=5
// 3. Return chunks (NOT LLM-synthesized — keep prompt-injection surface zero in v1)
// store_id derived from machine-auth JWT server-side (patch #10) — client body MUST NOT supply it.

POST { query: string, register_id }
auth: Bearer <machine-auth JWT>
returns: 200 { chunks: [{ id, title, content, score, doc_path, anchor }] }
```

Caches identical-query embeddings in Function memory (LRU, 5 min TTL) to cut cost.

#### `/functions/v1/help-faq-ingest` (admin)

Called only by `Tools/HelpIngest` CLI from a developer workstation. Authenticated via service-role bearer. Accepts batched `{ chunks: [...] }`, calls OpenAI embed, upserts into `public.help_faq` keyed by `(doc_path, anchor)`.

### 4.5 Outbox integration

`help_tickets` is a normal local table. On `INSERT`, existing `PushService` writes a `sync_queue` row. `OutboxRouter` drains by looking up `TableMapping` for `help_tickets`.

**Router refactor required (Phase 4 spike scope, post-validation patch #1):**
- Today `OutboxRouter` holds a single concrete `GenericSink _sink` field. Refactor to inject `IReadOnlyDictionary<SinkKind, ISink>` keyed by `SinkKind`. Define `ISink` interface around `UpsertAsync(...)` and friends.
- Extend `TableMapping` record with new `Sink` property of type `SinkKind` (default `Generic`). Update all 18 existing mappings in `TableMappings.cs` to set `Sink = SinkKind.Generic` explicitly.
- Confirm `PushService.SyncedTables` enumeration includes `"help_tickets"` (Phase 1 verify before any UI work). If not present, the trigger that emits `sync_queue` rows will not fire and tickets vanish silently.

```csharp
// In TableMappings.cs
table["help_tickets"] = new TableMapping {
    Sink = SinkKind.Http,                  // NEW kind
    Endpoint = "/functions/v1/help-report",
    AuthMode = AuthMode.MachineJwt,
    PayloadShaper = new HelpReportPayloadShaper(),
};
```

`HttpSink` (new, implements `ISink`) sits next to `GenericSink`. Reuses existing `BackoffPolicy.cs` for retries.

Response handling (post-validation patch #4 + #8):
- **2xx** → mark local row `status='sent'`
- **409 Conflict** with existing `ticket_no` → treat as success, mark `status='sent'` (idempotent retry path after partial network failure)
- **4xx** (validation) → mark `status='failed'`, do not retry
- **5xx / network** → backoff retry, increment `sync_attempts`
- **`last_error` field** → store only HTTP status code + short category string (e.g. `"429 rate_limit"`, `"5xx network"`). NEVER the raw response body — server-side validation errors may echo request payload back unredacted, which would leak PII into local logs.

### 4.6 Keyboard routing

`KeyboardRouter.cs` stays stateless. Add one predicate:

```csharp
public static bool IsCtrlSlash(KeyEventArgs e) =>
    e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
    (e.Key == Key.Oem2 || e.Key == Key.OemQuestion);  // both for layout safety
```

`ShellWindow.OnKeyDown` checks `IsCtrlSlash(e)` and calls `BantuanOverlayHost.Toggle()`. When overlay is open, it captures `Shift+Tab`, `Esc`, digit picks, `Enter`.

Update `kasir-pos/docs/KEYBOARD.md` with new bindings.

### 4.7 Overlay rendering

Reuse the existing `OverlayHost` `ContentControl` in `ShellWindow.axaml` (line 58). No new `Popup`. `BantuanOverlayHost`:

```csharp
public void Toggle() {
    if (_visible) { Close(); return; }
    _shellWindow.OverlayHost.Content = _strip;
    _shellWindow.OverlayHost.IsVisible = true;
    _previousFocus = FocusManager.Instance.Current;
    _strip.Focus();
}
public void Close() {
    _shellWindow.OverlayHost.IsVisible = false;
    _shellWindow.OverlayHost.Content = null;
    _previousFocus?.Focus();
}
```

Glass: `ExperimentalAcrylicBorder` with light-intensity tokens. At app startup, `AcrylicProbe.IsSupported()` runs once (try-render an offscreen surface, catch). Result cached to `Settings`. If unsupported → render a flat tinted `Border` with the same brand-soft fill. Win7 SP1 fallback covered.

Probe is wrapped in a 250ms hard timeout (post-validation patch #5) so a hung GPU driver on Win7 does not stall app startup. Timeout = treat as "unsupported", proceed with flat fallback.

Glass tokens (light intensity per design Q3):
- blur 6px, saturate 120%, alpha 88%
- `BackgroundSource = Digger` for Avalonia native acrylic
- Border: pre-computed RGBA of `color-mix(brand 38%, transparent)` per theme
- Shadow: drop shadow `0 8px 24px rgba(0,0,0,.18)`

### 4.8 State machine

Orthogonal axes per architect's S2:

```csharp
public enum BantuanMode { Tanya, Lapor }
public enum BantuanPhase { Hidden, Idle, Input, Working, Result, Confirm, Sent, Error }
public enum BantuanConnectivity { Online, Offline, AiDown }
```

15 wireframe cells = product of these. Renderer dispatches on `(mode, phase, connectivity)`. Impossible states (e.g., `Lapor + Disambiguate`) are simply never produced — no defensive code needed.

**Design-fidelity callouts (G1–G3 from second design audit):**

- **G1 Voice state**: Functionality skipped v1. State renderer exists (matches design: animated waveform, `0:04` timer, pulsing red dot, italic transcript text) but is **unreachable** from any real transition. Hidden behind `Settings.VoiceEnabled = false`. Keeps visual fidelity in handoff matches without shipping mic capture.

- **G2 Guided state**: Cashier presses a real F-key (e.g. F8) at the actual register and the guided card auto-advances. `BantuanViewModel` subscribes via `KeyboardRouter` event hook:
  ```csharp
  // HelpService.WatchKeyAsync(Key.F8, TimeSpan.FromSeconds(30))
  // returns Task<bool> — true when matched, false on timeout/cancel
  ```
  When watching, the strip shows pulsing `radio` icon + "Langkah X / Y" counter (matching design). On match → advance to next step or transition to Answer. On timeout → fall back to Answer state with manual instructions.

- **G3 Followup state**: Prior Q&A is NOT flushed — it stays visible above the new question, collapsed to one line with `corner-down-right` icon + dashed bottom border + faded prior-answer summary + elapsed-time tag (e.g. "0,8 dtk"). `BantuanViewModel` keeps a `LastTurn` record (Question, AnswerSummary, ElapsedMs) for rendering. Cleared on `Esc` close, not on Followup itself.

### 4.9 Hybrid retrieval flow

```
TANYA query:
  1. local FTS5 query → top-5 chunks + BM25 scores (always runs, ~10ms)
  2. if Online: HTTPS to /help-ask → top-5 vector chunks
  3. RRF fuse:  score(d) = Σ 1/(60 + rank_i(d))
  4. confidence = top1.score / (top1.score + top2.score)
     - ≥ 0.7  → Answer (single best chunk)
     - ≥ 0.4 + 2 candidates → Disambiguate
     - < 0.4  → NoAnswer
  5. hard cap: 1.5s p95. If exceeded → degrade to FTS5-only result.
```

`Online` detection: 1s probe to Supabase `/auth/v1/health`. Cache 30s.

### 4.10 LAPOR flow

```
1. cashier types body, picks category chip
2. BantuanViewModel → Confirm phase (show summary + scrubbed attachments)
3. confirm → ContextCollector builds attachments → PiiScrubber runs
4. HelpTicketRepository.Insert → trigger emits sync_queue row
5. UI shows Sent toast (#TKT-SM-01-260507-0001 — locally generated)
6. OutboxRouter drains → HttpSink POSTs to /help-report
7. on 2xx: mark sent. on persistent 4xx: mark failed, surface in Admin > Pending Reports.
```

`PiiScrubber` strips:
- PAN-like 13–19 digit sequences
- Rupiah amounts (`Rp\s?[\d.,]+`)
- 4–6 digit standalone numbers (PIN-like)
- Replaces with `[redacted]` and logs replacement count

Scrubber runs **deep/recursive** on all string values in `attachments` (patch #9). Top-level body, nested attachment fields (`last_error`, future `activity_log_30m` entries, any string in JSON tree). Tested with nested arrays + objects.

### 4.11 ticket_no format

`TKT-{store_short}-{reg}-{yymmdd}-{seq4}` e.g. `TKT-SM-01-260507-0001`.
- `store_short` from `Settings.StoreShort` (constant, e.g. `SM`)
- `reg` from `Settings.RegisterId` zero-padded width-2
- `seq4` per-register daily counter, persisted in `numbering` table (existing pattern)

Collision-free across registers by construction. Idempotent on Supabase via UNIQUE constraint.

## 5. Implementation steps

Each phase = green build + tests before next.

### Phase 1 — Schema
1. `Migration_006.cs` (class, IMigration). Tests: schema integrity, FTS5 round-trip.
2. `db/supabase/001_help_schema.sql` written + applied to project once via psql.
3. `HelpTicketRepository.cs` + `HelpFaqRepository.cs` + tests.

### Phase 2 — Doc ingester
4. `DocIngester.cs` (Markdig). Chunk by `##`/`###`. Local-only insert (no embed call).
5. `Tools/HelpIngest/` CLI. Run at install for local FTS5 hydrate. Separate flag `--remote` calls `/help-faq-ingest`.
6. Bundle starter FAQ markdown in `Kasir.Avalonia/Assets/Help/`.

### Phase 3 — Edge Functions
7. Write `functions/help-report/`, `functions/help-ask/`, `functions/help-faq-ingest/`.
8. Deploy via `supabase functions deploy`. Smoke-test with curl.
9. Apply rate-limit table via `001_help_schema.sql` extension.

### Phase 4 — CloudSync wiring (router refactor + integrate)
10. **Router refactor (patch #1):** define `ISink` interface around `UpsertAsync`. Refactor `OutboxRouter` ctor to take `IReadOnlyDictionary<SinkKind, ISink>` instead of single `_sink` field. Tests stay green for existing 18 tables.
11. **TableMapping extension (patch #2):** add `Sink` property of type `SinkKind` (default `Generic`). Set explicitly on all 18 existing mappings.
12. **PushService verify (patch #3):** confirm `SyncedTables` includes `"help_tickets"`. If not, add it. Manually verify trigger fires by inserting a row and reading `sync_queue`.
13. `HttpSink.cs` implements `ISink`. Reuse `BackoffPolicy`. Status handling per §4.5: 2xx + 409 → sent, 4xx → failed, 5xx/network → retry. `last_error` = status code + short category, never raw body.
14. Register `help_tickets` mapping in `TableMappings.cs` with `Sink = SinkKind.Http`.
15. Integration test: insert local row → drain → row appears in Supabase. Use a test category like `'_test_'` and clean up.

### Phase 5 — Retrieval + scrubber
14. `FtsIndex.cs` query wrapper + tests (Indonesian sample queries).
15. `HelpAskClient.cs` HTTPS client with timeouts.
16. `HybridRetriever.cs` + RRF + degrade-to-FTS5 path.
17. `PiiScrubber.cs` + extensive regex tests.
18. `ContextCollector.cs`.

### Phase 6 — UI shell + state machine
19. `BantuanOverlayHost.cs` mounting into existing `ShellWindow.OverlayHost`.
20. Acrylic probe + flat fallback.
21. `BantuanViewModel.cs` (Mode × Phase × Connectivity).
22. `BantuanGlassStrip.axaml` shell with one Idle state.
23. `KeyboardRouter.IsCtrlSlash` predicate. Wire `Ctrl+/` toggle in `ShellWindow.OnKeyDown`.

### Phase 7 — Components inside shell
24. `ModePill` — includes inline `⇧⇥` keyboard hint glyph per design (G6).
25. `Chip`, `Shimmer`, `SyncBadge`, `SentToast`. Each lands inside the working overlay.
26. Theme tokens: `--brand-soft`, glass styles in `Themes/BaseTheme.axaml`.
27. **G4 Hidden-state hint:** audit all screens' status bars (`Forms/POS/SaleView.axaml`, `MainMenuView.axaml`, every other view with a status bar) and add brand-teal `Ctrl+/ Bantuan` token. Prefer extracting to a shared `StatusBarHints` user control if 5+ screens need editing — single source of truth. Pre-trigger affordance must be present so cashier discovers Bantuan exists.

### Phase 8 — 15 states
28. Implement state renderers one by one in this order: Hidden, Idle, Typing, Thinking, Answer, NoAnswer, Disambiguate, Followup (G3: prior turn collapsed above), Guided (G2: live F-key watch via `HelpService.WatchKeyAsync`), Composing, Voice (G1: rendered but unreachable; mic disabled), Confirm, Sent, Offline, AiDown.
29. Wire `HelpService.AskAsync` and `HelpService.ReportAsync` to UI events.

### Phase 9 — Verify
28. Manual desktop walkthrough of every state.
29. `code-reviewer` agent.
30. `database-reviewer` re-validation on final schema + Edge Functions.
31. `security-reviewer` re-validation on PiiScrubber + Edge Functions + .env hygiene.
32. Update `KEYBOARD.md`, `CHANGELOG.md`.

## 6. Risks + mitigations

| Risk | Mitigation |
|------|-----------|
| Avalonia acrylic unsupported on Win7 SP1 | Probe at startup, flat tinted fallback |
| Edge Function cold start adds latency | Hard cap 1.5s p95, degrade to local FTS5 |
| OpenAI embedding cost spike | Edge Function caches identical queries 5min LRU. Embeddings cached in `public.help_faq.embedding` per chunk. |
| Supabase free-tier limits | 500MB DB / 5GB egress. ~2KB / ticket. 80k/yr fits. Monitor dashboard. |
| HNSW build slow on first deploy | 500 chunks builds in <2s. Acceptable. |
| Cashier waits during Thinking | Hard 1.5s cap + degrade |
| Anon key extracted from binary | Anon key is not powerful — only allows calling Edge Functions, which validate machine-auth JWT. Stolen anon key alone cannot file or read tickets. |
| Machine-auth refresh token leaked | Per-store account; rotate via Supabase Auth admin API. Detect anomalous IP via `register_id` mismatch in Edge Function logs. |
| PiiScrubber misses something | Logs every replacement count; periodic audit on `attachments` JSONB in Supabase to catch drift. |
| store_id ergonomics day 1 | Hardcode `Settings.StoreId='sinar-makmur'`, `StoreShort='SM'`. Single config flip when expanding. |

## 7. Success criteria

- All 15 states render matching design tokens (within 4px tolerance)
- `Ctrl+/` opens within 100ms from any screen
- TANYA returns answer within 1.5s p95 (offline FTS5: <50ms; online hybrid: <1.2s)
- LAPOR ticket survives offline → online round-trip; appears in Supabase within 60s of reconnect
- `help_tickets.body` insertion blocked at <3 chars or >2000 chars (both DB + Edge Function checked)
- 80%+ test coverage on `Kasir.Core/Help/`
- `dotnet build` green on Windows + macOS + Linux CI
- `code-reviewer`, `security-reviewer`, `database-reviewer` all return zero high-severity findings on re-validation

## 8. Validation gates

This plan must be re-validated by 3 subagents before any code is written:
1. `architect` — confirms outbox reuse via `sync_queue` + `HttpSink` integration
2. `database-reviewer` — confirms `unicode61` tokenize, HNSW, TEXT dates, all indexes
3. `security-reviewer` — confirms Edge Function trust boundary, anon key safety, PiiScrubber adequacy, rate limits

If any returns blocker findings → revise, re-validate.

## 9. Open follow-ups (post-v1)

- Realtime ack push (Supabase realtime → toast on cashier register)
- Activity log capture (ringbuffer of last 30min) — high PII risk, design scrubber first
- IT-side ticket dashboard (separate Next.js app or Supabase Studio extension). Body field renders `textContent` only.
- Multi-store: enable RLS with `(auth.jwt() ->> 'store_id')` claim path
- Voice dictation via Windows SAPI (id-ID) once Win7 acrylic settled
- LLM answer synthesis (TANYA): wrap chunks in delimited context block + system instruction guarding against prompt injection
