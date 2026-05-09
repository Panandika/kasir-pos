# Cloud Sync Decisions Log

Extracted from `plans/answer.md` (2026-04). Source of truth for cloud-sync planning Q&A.

| # | Question | Decision |
|---|----------|----------|
| 1 | Phase 6.1 (local ASP.NET API) vs 6.2 (cloud sync) — order? | **C. Both in parallel.** Local DB functionality stays primary; cloud-sync is mirror layer. |
| 2 | Phase 5 (production cutover) status? | **Done.** |
| 3 | Gateway host (Register 01 service / mini-PC / NAS)? | **OPEN — see GitHub issue.** User asked for explanation; not in current doc. |
| 4 | Supabase region + PII? | **Singapore region.** No PII in sales/customers tables. |
| 5 | Initial mirror scope (Phase A only vs full A→E)? | **Full A→E rollout, single plan.** |
| 6 | Litestream — same plan or split? | **Same plan, parallel track.** Reason: Postgres mirror covers query/dashboard reads, Litestream is byte-level disaster-recovery for the SQLite source itself. Different failure modes — keep both. |
| 7 | Outbox `cloud_synced` columns — migration safety across 3 registers? | **Open — verify existing LAN sync tolerates unknown columns before deploy.** |
| 8 | FTS5 in cloud — defer or day-1? | **Day 1.** Choose between FTS5 (SQLite-side mirror) vs tsvector (Postgres native) — currently leaning tsvector since cloud is Postgres. |
| 9 | Initial load: pgloader vs one-shot C# loader? | **Open — research pending.** |
| 10 | MVP consumer (dashboard scope)? | **Just mirror being queryable.** No specific dashboard required for Phase 1 done. |
| 11 | Dashboard auth? | **Shared password.** |
| 12 | Cost ceiling? | **Free tier only.** Supabase free (500 MB cap), R2 free tier for Litestream. |
| 13 | Timeline? | **No timeline.** Build it properly, not rushed. |

## Open items remaining

- Q3: Gateway host decision — tracked as GitHub issue.
- Q7: Outbox column-tolerance audit — needs verification before first cloud-sync deploy.
- Q9: Initial-load tool research — needs subagent research run.
