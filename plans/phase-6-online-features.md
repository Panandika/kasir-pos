# Phase 6: Optional — Online Features (Future)

**Duration:** After cutover is stable (3+ months)
**Dependency:** Phase 5 complete, system stable in production
**Commit strategy:** One commit per feature. No AI co-author tags.

Example commits:
- `"Phase 6.1: ASP.NET Web API on hub machine"`
- `"Phase 6.2: SQLite ↔ PostgreSQL sync bridge"`
- `"Phase 6.3: web dashboard — remote sales/inventory monitoring"`
- `"Phase 6.4: multi-store support"`

---

## Objective

Add optional online/remote capabilities. This phase is entirely optional and only pursued after the core system is stable in production. The system must continue to work 100% offline — online features are additive.

---

## Steps

### Step 6.1: ASP.NET Web API

**What:** REST API on the hub machine for remote access.

**Stack:**
- ASP.NET Web API 2 (ships with .NET Framework 4.8)
- Self-hosted via OWIN/Katana (no IIS needed)
- Runs on hub register (Register 01) as a Windows service or background process
- Authenticated via API key or basic auth

**Endpoints:**
```
GET  /api/products              — product list (paginated)
GET  /api/products/{code}       — product detail
GET  /api/sales/daily           — daily sales summary
GET  /api/sales/range?from=&to= — sales in date range
GET  /api/stock                 — stock positions
GET  /api/stock/{code}          — stock for product
GET  /api/reports/trial-balance — trial balance
GET  /api/reports/pl            — P&L
GET  /api/status                — system status (registers, last sync, etc.)
```

**Security:**
- HTTPS only (self-signed cert acceptable for local network)
- API key in header: `X-API-Key: <secret>`
- Rate limiting: 100 requests/minute
- Read-only initially — no POST/PUT/DELETE

**Exit criteria:** API serves data from hub's SQLite. Accessible from LAN.

### Step 6.2: SQLite ↔ PostgreSQL Sync Bridge

**What:** Optional central PostgreSQL database for long-term storage and analytics.

**Architecture:**
```
Registers (SQLite)  →  Hub (SQLite)  →  PostgreSQL Server
                                          ↓
                                    Web Dashboard
```

**Implementation:**
- Hub periodically exports transaction data to PostgreSQL
- One-way sync: SQLite → PostgreSQL (no write-back)
- PostgreSQL stores historical data for analytics
- Registers continue to use SQLite locally — PostgreSQL is additive

**PostgreSQL version:** Latest that runs on available hardware (or cloud-hosted)

**Exit criteria:** Transaction data flows to PostgreSQL. Historical queries work.

### Step 6.3: Web Dashboard

**What:** Remote monitoring dashboard accessible from any browser.

**Stack:**
- Simple HTML/JS frontend (no heavy framework needed)
- Fetches data from ASP.NET Web API (Step 6.1)
- Or: direct PostgreSQL queries if Step 6.2 is done

**Dashboard pages:**
- Today's sales summary (total, transaction count, by register)
- Stock alerts (products below minimum)
- AP summary (total outstanding, overdue)
- System status (register connectivity, last sync time)

**Exit criteria:** Owner can view sales/stock remotely from phone or laptop.

### Step 6.4: Multi-Store Support (Future)

**What:** If the business opens additional stores.

**Architecture changes:**
- Each store has its own hub + registers
- Inter-store sync via PostgreSQL or cloud service
- Store-level reporting + consolidated reporting
- Separate inventory per store with transfer capability

**This is a major architecture extension — plan separately when needed.**

**Exit criteria:** N/A — future planning only.

---

## Phase 6 Deliverable

Optional: REST API for remote access + web dashboard for monitoring. System continues to work 100% offline without these features.

---

## Important Note

Phase 6 is entirely optional. The core system (Phases 0-5) is complete and functional without any online features. Only pursue Phase 6 after:
1. Core system stable in production for 3+ months
2. Business need identified (remote monitoring, multi-store)
3. Hardware available (server for PostgreSQL, or cloud hosting)
