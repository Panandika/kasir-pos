// Edge Function: /functions/v1/help-ask
//
// Cashier register → ask the AI a question.
//
// Pipeline:
//   1. Validate JWT, derive store_id (NEVER from client body — patch #10)
//   2. Embed query via OpenAI text-embedding-3-small (key held here only)
//   3. Vector search on public.help_faq via pgvector cosine
//   4. Return raw chunks. NO LLM synthesis in v1 — zero prompt-injection surface.
//
// In-memory LRU cache 5min keyed by trimmed lowercase query string to cut
// embedding cost on repeated identical queries.

import { adminClient, requireMachineContext, jsonResponse, preflight } from "../_shared/auth.ts";

const OPENAI_API_KEY = Deno.env.get("OPENAI_API_KEY") ?? "";
const EMBED_MODEL = "text-embedding-3-small";

// crude LRU
const CACHE_MAX = 256;
const CACHE_TTL_MS = 5 * 60 * 1000;
interface CacheEntry { embedding: number[]; expiresAt: number }
const embedCache = new Map<string, CacheEntry>();

async function embed(text: string): Promise<number[]> {
  const key = text.trim().toLowerCase();
  const now = Date.now();
  const hit = embedCache.get(key);
  if (hit && hit.expiresAt > now) {
    embedCache.delete(key);
    embedCache.set(key, hit);
    return hit.embedding;
  }

  const r = await fetch("https://api.openai.com/v1/embeddings", {
    method: "POST",
    headers: {
      "authorization": `Bearer ${OPENAI_API_KEY}`,
      "content-type": "application/json",
    },
    body: JSON.stringify({ model: EMBED_MODEL, input: text }),
  });
  if (!r.ok) {
    throw new Error(`openai_embed_failed_${r.status}`);
  }
  const json = await r.json() as { data: { embedding: number[] }[] };
  const vec = json.data[0].embedding;

  if (embedCache.size >= CACHE_MAX) {
    const firstKey = embedCache.keys().next().value;
    if (firstKey) embedCache.delete(firstKey);
  }
  embedCache.set(key, { embedding: vec, expiresAt: now + CACHE_TTL_MS });
  return vec;
}

interface AskPayload {
  query: string;
  register_id?: string;
}

Deno.serve(async (req) => {
  if (req.method === "OPTIONS") return preflight();
  if (req.method !== "POST") return jsonResponse({ error: "method_not_allowed" }, 405);

  const ctx = await requireMachineContext(req);
  if (!ctx) return jsonResponse({ error: "unauthorized" }, 401);

  let payload: AskPayload;
  try {
    payload = await req.json();
  } catch {
    return jsonResponse({ error: "invalid_json" }, 400);
  }
  if (!payload.query || payload.query.trim().length < 2) {
    return jsonResponse({ error: "query_required" }, 400);
  }

  if (!OPENAI_API_KEY) {
    return jsonResponse({ error: "ai_unavailable", chunks: [] }, 503);
  }

  let qVec: number[];
  try {
    qVec = await embed(payload.query);
  } catch {
    return jsonResponse({ error: "embed_failed", chunks: [] }, 503);
  }

  const sb = adminClient();
  // pgvector cosine — `<=>` is cosine distance (lower = better)
  // Use rpc helper if you've added one; here we use a raw SQL via postgrest extension.
  const { data, error } = await sb.rpc("help_faq_search", {
    query_embedding: qVec,
    match_count: 5,
  });
  if (error) {
    // Fallback: return empty list, register will degrade to local FTS5
    return jsonResponse({ chunks: [], note: "vector_search_failed" }, 200);
  }

  return jsonResponse({ chunks: data ?? [] }, 200);
});
