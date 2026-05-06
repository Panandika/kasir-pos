// Shared helpers for Edge Functions: machine-auth JWT validation, store_id derivation.
//
// Trust model: register PCs only ever hold the anon key + a Supabase Auth machine
// account refresh token. Edge Functions read the *user* JWT from Authorization,
// derive store_id from custom claims set during machine signup, and bypass RLS via
// SUPABASE_SERVICE_ROLE_KEY when writing.

import { createClient, SupabaseClient } from "https://esm.sh/@supabase/supabase-js@2.39.7";

export interface MachineContext {
  storeId: string;
  registerId: string | null;
  userId: string;
}

const SUPABASE_URL = Deno.env.get("SUPABASE_URL") ?? "";
const SERVICE_ROLE_KEY = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY") ?? "";

export function adminClient(): SupabaseClient {
  if (!SUPABASE_URL || !SERVICE_ROLE_KEY) {
    throw new Error("Missing SUPABASE_URL / SUPABASE_SERVICE_ROLE_KEY env");
  }
  return createClient(SUPABASE_URL, SERVICE_ROLE_KEY, {
    auth: { persistSession: false, autoRefreshToken: false },
  });
}

/**
 * Validate the request's Authorization bearer token via Supabase Auth and
 * return the machine context (storeId, registerId from app_metadata).
 * Returns null on missing/invalid token. Caller should respond 401 on null.
 */
export async function requireMachineContext(req: Request): Promise<MachineContext | null> {
  const auth = req.headers.get("authorization") ?? req.headers.get("Authorization");
  if (!auth || !auth.startsWith("Bearer ")) return null;
  const token = auth.slice(7);

  const sb = adminClient();
  const { data, error } = await sb.auth.getUser(token);
  if (error || !data.user) return null;

  const meta = (data.user.app_metadata ?? {}) as Record<string, unknown>;
  const storeId = typeof meta.store_id === "string" ? meta.store_id : null;
  const registerId = typeof meta.register_id === "string" ? meta.register_id : null;
  if (!storeId) return null;

  return {
    storeId,
    registerId,
    userId: data.user.id,
  };
}

/**
 * Standard JSON response helper. Adds CORS headers so the function can be
 * called from local dev without faff.
 */
export function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      "content-type": "application/json",
      "access-control-allow-origin": "*",
      "access-control-allow-headers": "authorization, content-type",
      "access-control-allow-methods": "POST, OPTIONS",
    },
  });
}

export function preflight(): Response {
  return jsonResponse({}, 204);
}
