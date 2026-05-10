# Supabase — Bantuan Help Assistant

> **Note:** Schema migrations have moved to `sinar-makmur-dashboard/supabase/migrations/0031_*` and `0032_*`. This directory now contains only Edge Functions; the `001_*.sql` and `002_*.sql` files here are stub redirects, not active migrations.

Server-side artefacts for the Bantuan feature: schema, RPC, Edge Functions.
Registers never run any of this — they call the Edge Functions over HTTPS.

## Layout

```
db/supabase/
├── 001_help_schema.sql           Tables + indexes + extensions
├── 002_help_faq_search_rpc.sql   pgvector cosine search RPC
└── functions/
    ├── _shared/auth.ts           JWT + admin client helpers
    ├── help-report/index.ts      Cashier ticket submission
    ├── help-ask/index.ts         AI Q&A (embed + vector search)
    └── help-faq-ingest/index.ts  Admin: ingest FAQ corpus + embeddings
```

## Apply schema

Use the Supavisor session pooler from `kasir-pos/.env` (NOT registers):

```bash
psql "$CONNECTION_STRING" -f db/supabase/001_help_schema.sql
psql "$CONNECTION_STRING" -f db/supabase/002_help_faq_search_rpc.sql
```

## Deploy Edge Functions

Requires the [Supabase CLI](https://supabase.com/docs/guides/cli) and a
linked project (`supabase link --project-ref mnatezzsysmadvrosnad`):

```bash
cd db/supabase
supabase functions deploy help-report
supabase functions deploy help-ask
supabase functions deploy help-faq-ingest
```

Set function secrets (one-time):

```bash
supabase secrets set OPENAI_API_KEY=sk-...
# SUPABASE_URL and SUPABASE_SERVICE_ROLE_KEY are set automatically by Supabase
```

## Machine auth setup (one-time per register)

Each register signs in once with a per-store machine account, then stores the
refresh token locally. Custom claims `store_id` and `register_id` are set in
`app_metadata` via the admin API:

```ts
// run once on dev workstation
const sb = createClient(SUPABASE_URL, SERVICE_ROLE_KEY);
await sb.auth.admin.createUser({
  email: "register-01@sinar-makmur.local",
  password: <strong-random>,
  email_confirm: true,
  app_metadata: { store_id: "sinar-makmur", register_id: "01" },
});
```

The register's auth client logs in with email+password once, then keeps the
refresh token in OS-level secure storage (Windows Credential Manager). Anon
key is used as the project-level `apikey` header; the user JWT carries the
machine identity.

## Testing the deploy (curl)

After login, save the access token; then:

```bash
ACCESS_TOKEN=$(...)  # from supabase auth.signInWithPassword

# Submit a test ticket
curl -X POST "$SUPABASE_URL/functions/v1/help-report" \
  -H "apikey: $ANON_KEY" \
  -H "authorization: Bearer $ACCESS_TOKEN" \
  -H "content-type: application/json" \
  -d '{
    "ticket_no": "TKT-SM-01-260507-test1",
    "category": "hardware",
    "body": "test ticket from curl",
    "attachments": {"version":"2.4.1"}
  }'
```

## Removing the schema (clean slate)

```sql
drop function if exists public.help_faq_search(vector, int);
drop table if exists public.help_rate_limits;
drop table if exists public.help_faq;
drop table if exists public.help_tickets;
drop function if exists public.help_rate_limits_trim();
```

Functions: `supabase functions delete help-report help-ask help-faq-ingest`.
