# Supabase — Bantuan Help Assistant (relocated)

All server-side Bantuan artefacts have moved to the **`sinar-makmur-dashboard`** repo so a single CI pipeline applies migrations + deploys Edge Functions to the same Supabase project.

## Where things live now

| Artefact | New location |
|---|---|
| Schema | `sinar-makmur-dashboard/supabase/migrations/0031_help_schema.sql` |
| RPC `help_faq_search` | `sinar-makmur-dashboard/supabase/migrations/0032_help_faq_search_rpc.sql` |
| Edge Function `help-report` | `sinar-makmur-dashboard/supabase/functions/help-report/index.ts` |
| Edge Function `help-ask` | `sinar-makmur-dashboard/supabase/functions/help-ask/index.ts` |
| Edge Function `help-faq-ingest` | `sinar-makmur-dashboard/supabase/functions/help-faq-ingest/index.ts` |
| Auth helpers | `sinar-makmur-dashboard/supabase/functions/_shared/auth.ts` |
| Deploy CI | `sinar-makmur-dashboard/.github/workflows/migrate.yml` + `deploy-functions.yml` |

The two `001_*.sql` and `002_*.sql` files in this directory are now redirect stubs; **do not apply them**.

## Why

Edge Functions and migrations both deploy to the same Supabase project (`mnatezzsysmadvrosnad`). Keeping them in the dashboard repo means:

- Single CI run handles both on merge to `main`
- One source of truth for DB schema + function code
- No cross-repo deploy coordination

## Runtime secret

Embeddings provider is OpenRouter (OpenAI-compatible). One-time setup:

```bash
supabase secrets set OPENROUTER_KEY=sk-or-v1-... --project-ref mnatezzsysmadvrosnad
```

## Register-side code

The C# Avalonia client (this repo) still owns:

- `Kasir.Core/Help/` — local FTS5, retrieval, sync, ticket queue
- `Kasir.Core/Help/Auth/SupabaseMachineAuth.cs` — machine-user JWT bootstrap
- `Kasir.Avalonia/Forms/Help/BantuanOverlayHost.cs` — `Ctrl+/` overlay

The register calls the deployed Edge Functions over HTTPS using the anon key + machine-user JWT.

## Machine auth setup (one-time per register)

```ts
// run once on dev workstation against the dashboard repo's Supabase project
const sb = createClient(SUPABASE_URL, SERVICE_ROLE_KEY);
await sb.auth.admin.createUser({
  email: "register-01@sinar-makmur.local",
  password: <strong-random>,
  email_confirm: true,
  app_metadata: { store_id: "sinar-makmur", register_id: "01" },
});
```

Drop the credentials into `%APPDATA%\Kasir\help.json` on each register. See `HelpConfig` for the schema.
