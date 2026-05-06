-- RPC used by /functions/v1/help-ask for vector cosine search.
-- Returns top N chunks ranked by cosine similarity (1 - distance).

create or replace function public.help_faq_search(
  query_embedding vector(1536),
  match_count int default 5
)
returns table (
  id          uuid,
  title       text,
  content     text,
  doc_path    text,
  anchor      text,
  score       double precision
)
language sql
stable
as $$
  select
    f.id,
    f.title,
    f.content,
    f.doc_path,
    f.anchor,
    (1 - (f.embedding <=> query_embedding))::double precision as score
  from public.help_faq f
  where f.embedding is not null
  order by f.embedding <=> query_embedding
  limit match_count;
$$;
