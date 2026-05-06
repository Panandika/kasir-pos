-- =============================================================
-- Bantuan Help Assistant — Supabase / Postgres schema
-- Idempotent: safe to run multiple times.
-- Apply once via psql: psql "$DIRECT_CONNECTION_STRING" -f 001_help_schema.sql
-- =============================================================

create extension if not exists vector;
create extension if not exists pgcrypto;

-- ─────────────────────────────────────────────────────────────
-- help_tickets — cashier-filed bug reports / feature requests
-- ─────────────────────────────────────────────────────────────
create table if not exists public.help_tickets (
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

create index if not exists help_tickets_store_status_idx on public.help_tickets (store_id, status);
create index if not exists help_tickets_recent_idx on public.help_tickets (server_created_at desc);

comment on column public.help_tickets.body is
  'Untrusted free-text from cashier. MUST be rendered with HTML escaping (textContent) in any UI. No HTML/markdown parsing.';
comment on column public.help_tickets.resolution_note is
  'Untrusted free-text from IT user. MUST be rendered with HTML escaping in any UI.';

-- ─────────────────────────────────────────────────────────────
-- help_faq — FAQ corpus with embeddings (pgvector + HNSW)
-- ─────────────────────────────────────────────────────────────
create table if not exists public.help_faq (
  id              uuid primary key default gen_random_uuid(),
  doc_path        text not null,
  anchor          text,
  title           text,
  content         text not null,
  tags            text[],
  embedding       vector(1536),
  updated_at      timestamptz not null default now()
);

-- HNSW chosen over ivfflat for <5k corpus: better recall, no rebuild on insert.
create index if not exists help_faq_embedding_idx on public.help_faq
  using hnsw (embedding vector_cosine_ops)
  with (m = 16, ef_construction = 64);

create unique index if not exists help_faq_path_anchor_idx
  on public.help_faq (doc_path, coalesce(anchor, ''));

-- ─────────────────────────────────────────────────────────────
-- help_rate_limits — per-register / per-store quotas
-- ─────────────────────────────────────────────────────────────
create table if not exists public.help_rate_limits (
  id              bigserial primary key,
  store_id        text not null,
  register_id     text not null,
  created_at      timestamptz not null default now()
);
create index if not exists help_rate_limits_window_idx
  on public.help_rate_limits (register_id, created_at desc);
create index if not exists help_rate_limits_store_idx
  on public.help_rate_limits (store_id, created_at desc);

-- Trim entries older than 24h to keep the table small.
create or replace function public.help_rate_limits_trim() returns void
language sql as $$
  delete from public.help_rate_limits where created_at < now() - interval '24 hours';
$$;

-- ─────────────────────────────────────────────────────────────
-- RLS deferred until multi-store. Edge Functions enforce store_id from JWT.
-- When ready: alter table public.help_tickets enable row level security;
--            create policy ... using ((auth.jwt() ->> 'store_id') = store_id);
-- ─────────────────────────────────────────────────────────────
