// Edge Function: /functions/v1/help-report
//
// Cashier register → submit a Bantuan ticket.
//
// Trust:
// - JWT verified server-side via Supabase Auth (machine account per store)
// - store_id derived from JWT app_metadata, NOT request body
// - Insert via SUPABASE_SERVICE_ROLE_KEY (RLS deferred)
//
// Rate limits: 5 tickets / register / hour, 50 / store / day.
// Idempotent: duplicate ticket_no returns 200 with existing row (409 sent
// only after the conflict is detected and resolved as success-on-retry).

import { adminClient, requireMachineContext, jsonResponse, preflight } from "../_shared/auth.ts";

interface TicketPayload {
  ticket_no: string;
  register_id?: string;
  cashier_id?: string;
  category: string;
  body: string;
  attachments?: Record<string, unknown>;
  client_created_at?: string;
}

const VALID_CATEGORIES = new Set(["hardware", "transaksi", "aplikasi", "saran"]);

Deno.serve(async (req) => {
  if (req.method === "OPTIONS") return preflight();
  if (req.method !== "POST") return jsonResponse({ error: "method_not_allowed" }, 405);

  const ctx = await requireMachineContext(req);
  if (!ctx) return jsonResponse({ error: "unauthorized" }, 401);

  let payload: TicketPayload;
  try {
    payload = await req.json();
  } catch {
    return jsonResponse({ error: "invalid_json" }, 400);
  }

  // Validation
  if (!payload.ticket_no || typeof payload.ticket_no !== "string") {
    return jsonResponse({ error: "ticket_no_required" }, 400);
  }
  if (!payload.category || !VALID_CATEGORIES.has(payload.category)) {
    return jsonResponse({ error: "invalid_category" }, 400);
  }
  if (!payload.body || payload.body.length < 3 || payload.body.length > 2000) {
    return jsonResponse({ error: "body_length_out_of_range" }, 400);
  }

  const sb = adminClient();
  const registerId = payload.register_id || ctx.registerId || "unknown";

  // Rate limit: 5 / register / hour
  {
    const { count, error } = await sb
      .from("help_rate_limits")
      .select("id", { count: "exact", head: true })
      .eq("register_id", registerId)
      .gt("created_at", new Date(Date.now() - 60 * 60 * 1000).toISOString());
    if (error) return jsonResponse({ error: "rate_limit_check_failed" }, 500);
    if ((count ?? 0) >= 5) return jsonResponse({ error: "rate_limit_register_hour" }, 429);
  }
  // Rate limit: 50 / store / day
  {
    const { count, error } = await sb
      .from("help_rate_limits")
      .select("id", { count: "exact", head: true })
      .eq("store_id", ctx.storeId)
      .gt("created_at", new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString());
    if (error) return jsonResponse({ error: "rate_limit_check_failed" }, 500);
    if ((count ?? 0) >= 50) return jsonResponse({ error: "rate_limit_store_day" }, 429);
  }

  const clientCreatedAt = payload.client_created_at && !isNaN(Date.parse(payload.client_created_at))
    ? payload.client_created_at
    : new Date().toISOString();

  // Insert. On unique-violation (existing ticket_no), fetch and return existing row as idempotent success.
  const insertRow = {
    ticket_no: payload.ticket_no,
    store_id: ctx.storeId,
    register_id: registerId,
    cashier_id: payload.cashier_id ?? null,
    category: payload.category,
    body: payload.body,
    attachments: payload.attachments ?? {},
    client_created_at: clientCreatedAt,
  };

  const { data: inserted, error: insertErr } = await sb
    .from("help_tickets")
    .insert(insertRow)
    .select("id, ticket_no, server_created_at")
    .single();

  if (insertErr) {
    if ((insertErr as { code?: string }).code === "23505") {
      // unique violation — return existing
      const { data: existing } = await sb
        .from("help_tickets")
        .select("id, ticket_no, server_created_at")
        .eq("ticket_no", payload.ticket_no)
        .single();
      if (existing) return jsonResponse(existing, 200);
    }
    return jsonResponse({ error: "insert_failed" }, 500);
  }

  // Record rate-limit usage. Best-effort; failure here doesn't fail the ticket.
  await sb.from("help_rate_limits").insert({
    store_id: ctx.storeId,
    register_id: registerId,
  });

  return jsonResponse(inserted, 200);
});
