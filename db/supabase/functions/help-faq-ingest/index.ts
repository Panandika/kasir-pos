// Edge Function: /functions/v1/help-faq-ingest
//
// Admin-only. Called from Tools/HelpIngest with --remote flag (developer
// workstation, never registers). Authenticated via service-role bearer.
// Accepts batched chunks, embeds via OpenAI, upserts into public.help_faq.

import { adminClient, jsonResponse, preflight } from "../_shared/auth.ts";

const OPENAI_API_KEY = Deno.env.get("OPENAI_API_KEY") ?? "";
const SERVICE_ROLE_KEY = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY") ?? "";
const EMBED_MODEL = "text-embedding-3-small";

interface ChunkPayload {
  doc_path: string;
  anchor?: string;
  title?: string;
  content: string;
  tags?: string[];
}

async function embed(text: string): Promise<number[]> {
  const r = await fetch("https://api.openai.com/v1/embeddings", {
    method: "POST",
    headers: {
      "authorization": `Bearer ${OPENAI_API_KEY}`,
      "content-type": "application/json",
    },
    body: JSON.stringify({ model: EMBED_MODEL, input: text }),
  });
  if (!r.ok) throw new Error(`openai_embed_failed_${r.status}`);
  const json = await r.json() as { data: { embedding: number[] }[] };
  return json.data[0].embedding;
}

Deno.serve(async (req) => {
  if (req.method === "OPTIONS") return preflight();
  if (req.method !== "POST") return jsonResponse({ error: "method_not_allowed" }, 405);

  // Admin-only: must present the service-role bearer
  const auth = req.headers.get("authorization") ?? "";
  if (!auth.startsWith("Bearer ") || auth.slice(7) !== SERVICE_ROLE_KEY) {
    return jsonResponse({ error: "unauthorized" }, 401);
  }

  if (!OPENAI_API_KEY) return jsonResponse({ error: "embed_unavailable" }, 503);

  let body: { chunks: ChunkPayload[] };
  try {
    body = await req.json();
  } catch {
    return jsonResponse({ error: "invalid_json" }, 400);
  }
  if (!Array.isArray(body.chunks) || body.chunks.length === 0) {
    return jsonResponse({ error: "chunks_required" }, 400);
  }

  const sb = adminClient();
  let upserted = 0;

  for (const c of body.chunks) {
    if (!c.doc_path || !c.content) continue;
    const inputText = (c.title ? c.title + "\n" : "") + c.content;
    let vec: number[];
    try {
      vec = await embed(inputText);
    } catch {
      return jsonResponse({ error: "embed_failed", upserted }, 503);
    }
    const { error } = await sb.from("help_faq").upsert(
      {
        doc_path: c.doc_path,
        anchor: c.anchor ?? null,
        title: c.title ?? null,
        content: c.content,
        tags: c.tags ?? null,
        embedding: vec,
        updated_at: new Date().toISOString(),
      },
      { onConflict: "doc_path,anchor" },
    );
    if (error) return jsonResponse({ error: "upsert_failed", upserted }, 500);
    upserted++;
  }

  return jsonResponse({ upserted }, 200);
});
