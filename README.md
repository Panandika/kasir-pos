# Kasir POS

[![Build and Test](https://github.com/Panandika/kasir-pos/actions/workflows/build.yml/badge.svg)](https://github.com/Panandika/kasir-pos/actions)
![.NET Framework 4.8](https://img.shields.io/badge/.NET_Framework-4.8-purple)
![C# 7.3](https://img.shields.io/badge/C%23-7.3-blue)
![SQLite](https://img.shields.io/badge/SQLite-WAL_mode-green)
![License: MIT](https://img.shields.io/badge/License-MIT-yellow)

A full-featured Point of Sale system built to replace a **30-year-old FoxPro/Harbour retail system** running on Windows 7 register hardware. Handles 24,000+ products, 750+ vendors, 80,000+ AP records, and 3 registers with offline-first sync.

<video src="docs/kasir-showcase.mp4" width="100%" autoplay muted loop></video>

## Why This Exists

RASIO POS has been running a single retail store in Indonesia since the mid-1990s. The original system was written in FoxPro 2.6, later modernized to Harbour 3.0 (266 compiled modules, 3.7MB executable). After 30 years, the platform risks became critical:

- **Windows XP/7 end of life** - no security updates
- **FoxPro discontinued** - no tooling, no community
- **dBASE format limitations** - 170 DBF files, no referential integrity
- **97.6% of POS journal data corrupted** - unrecoverable

This project migrates the entire system to a modern, maintainable stack while preserving every business rule refined over three decades of daily retail operations.

## Features

### Point of Sale
- Barcode scanning (HID keyboard input) with instant product lookup
- 5-tier pricing engine: promo > barcode override > open price > qty break > customer tier
- First-match discount engine: partner > discount table > product > account config
- Split payment: cash + card + voucher with live change calculation
- Loyalty points (1 sticker per Rp 10,000 spent)
- ESC/POS receipt printing with cash drawer kick pulse
- Shift management (open/close with cash count)

### Inventory & Purchasing
- Full purchase cycle: PO > Goods Receipt > Invoice > Return
- FIFO cost calculation with layer peeling
- Average cost tracking
- Stock transfers between locations (paired IN/OUT movements)
- Stock opname (physical count) with variance calculation
- Stock out tracking: usage, damage, loss

### Accounting & Reports
- Chart of accounts with hierarchy
- Journal posting with **debit = credit** enforcement (no exceptions)
- GL batch posting: sales, purchases, returns, cash transactions
- Fiscal period close with balance carry-forward
- AP management: payment allocation (oldest-first), aging report (current/30/60/90/120+)
- Bank giro (check) processing: clearing and rejection
- Cash receipt and disbursement forms

### Financial Reports
- Trial Balance (Neraca Saldo)
- Balance Sheet (Neraca)
- Profit & Loss (Laba/Rugi)
- AP Aging Report
- GL Detail (Buku Besar)
- All exportable to Excel via ClosedXML

### Multi-Register Sync
- Each register has its own local SQLite database (offline-first)
- Sync via HMAC-SHA256 signed JSON batches over SMB shared folder
- Master data: one-way hub > registers (no conflicts)
- Transactions: one-way each register > hub (partitioned by register ID)
- Automatic sync: push every 15s, pull every 60s

## Architecture

```
                    +-----------------+
                    |   WinForms UI   |
                    |  (33 Forms)     |
                    +--------+--------+
                             |
              +--------------+--------------+
              |                             |
     +--------v--------+          +--------v--------+
     |    Services      |          |    Hardware     |
     | (13 engines)     |          | ESC/POS Printer |
     | Pricing, Sales,  |          | Cash Drawer     |
     | Inventory, GL... |          | Barcode Scanner |
     +--------+---------+          +-----------------+
              |
     +--------v--------+
     |  Repositories    |
     |  (17 repos)      |
     +--------+---------+
              |
     +--------v--------+          +-----------------+
     |    SQLite DB     |  <--->  |   Sync Engine   |
     | 57 tables, WAL   |         | JSON over SMB   |
     | 28 triggers      |         | HMAC-SHA256     |
     +-----------------+          +-----------------+
```

```
  Register 01 (Hub)          Register 02          Register 03
  +--------------+          +-----------+        +-----------+
  | Master data  |  ------> | Local DB  |        | Local DB  |
  | Products     |  ------> | (sync)    |        | (sync)    |
  | Vendors      |          +-----------+        +-----------+
  | Prices       |               |                    |
  +--------------+               v                    v
       ^                    Sales data            Sales data
       |                    pushed to hub         pushed to hub
       +--------------------<---------------------<---+
```

## Tech Stack

| Technology | Version | Purpose |
|-----------|---------|---------|
| C# | 7.3 | Application language (max version for .NET 4.8) |
| .NET Framework | 4.8 | Runtime (supports Windows 7 SP1) |
| WinForms | - | Desktop UI with terminal-style theme |
| SQLite | WAL mode | Local database per register |
| System.Data.SQLite | 1.0.119 | SQLite driver with FTS5 + json1 |
| BCrypt.Net-Next | 4.1.0 | Password hashing (cost factor 10) |
| ClosedXML | 0.104.2 | Excel report export |
| Newtonsoft.Json | 13.0.4 | JSON serialization for sync |
| NUnit + FluentAssertions | 3.14 / 6.12 | Testing framework |
| GitHub Actions | - | CI: msbuild + vstest on Windows runner |

## Project Structure

```
kasir-pos/
+-- Kasir/                     # Main application (17,741 LOC)
|   +-- Auth/                  # Authentication + role-based permissions
|   +-- Data/
|   |   +-- Repositories/      # 17 repository classes (CRUD)
|   |   +-- Schema.sql         # 57 tables, 28 triggers, 68 indexes
|   |   +-- DbConnection.cs    # Singleton, WAL mode, FTS5 loading
|   |   +-- SqlHelper.cs       # Parameterized query helpers
|   +-- Forms/
|   |   +-- POS/               # Sale, Payment, Shift
|   |   +-- Master/            # Product, Vendor, Department, CreditCard, PriceChange
|   |   +-- Purchasing/        # PO, Goods Receipt, Invoice, Return
|   |   +-- Inventory/         # Stock Out, Transfer, Opname
|   |   +-- Accounting/        # Accounts, Journal, Payables, Cash, Posting
|   |   +-- Bank/              # Bank master, Giro processing
|   |   +-- Reports/           # Sales, Inventory, Financial reports
|   |   +-- Admin/             # Users, Printer, Backup
|   +-- Hardware/              # ESC/POS printer, cash drawer, barcode scanner
|   +-- Models/                # 35 domain models
|   +-- Services/              # 13 business logic engines
|   +-- Sync/                  # Multi-register sync (Push/Pull/Engine)
|   +-- Utils/                 # Formatting, validation, clock
+-- Kasir.Tests/               # 247 automated tests
+-- data/                      # SQLite database (gitignored)
+-- NETWORK-SETUP.md           # Multi-register network configuration guide
```

## Getting Started

### Prerequisites

- **Windows 7 SP1** or later (production targets Win7 registers)
- **Visual Studio 2022** with .NET Framework 4.8 targeting pack
- **.NET Framework 4.8** runtime

### Build

```bash
# Clone
git clone https://github.com/Panandika/kasir-pos.git
cd kasir-pos

# Restore + Build (use msbuild, NOT dotnet build)
msbuild Kasir.sln /t:Restore /p:Configuration=Release /p:Platform=x86
msbuild Kasir.sln /p:Configuration=Release /p:Platform=x86
```

### Run

```bash
# From Visual Studio: F5
# Or directly:
Kasir\bin\Release\Kasir.exe
```

The app creates a fresh database with schema on first run. To use migrated production data, place `kasir.db` in the `data/` folder next to the executable.

### Deploy to Register

```bash
# Copy entire bin/Release/ folder to target PC
xcopy /E /I bin\Release\ \\REGISTER02\kasir\app\
```

## Data Migration

The legacy system's 30 years of data (170 DBF files) was migrated using a Python script:

```bash
cd migration/
python3 migrate.py /path/to/main-kasir ./output/kasir.db
```

| Metric | Value |
|--------|-------|
| Source files | 119 DBF files (58 MST + 61 TRS) |
| Files with data | 30 tables |
| Rows migrated | 343,691 |
| Migration time | 11 seconds |
| Output size | 55 MB |
| Success rate | 99.9% (83 corrupt dates skipped) |

Key migrations: 24,455 products, 754 vendors, 194 departments, 76,213 price history records, 80,557 AP records, 68,101 purchase line items, 26,319 payment records.

## Testing

```bash
# Run tests via vstest
vstest.console.exe Kasir.Tests\bin\Release\Kasir.Tests.dll
```

**247 tests** covering:
- PricingEngine (5-tier price resolution)
- DiscountEngine (first-match-wins from 4 sources)
- PaymentCalculator (split payment, loyalty points)
- SalesService (full POS transaction lifecycle)
- AccountingService (debit=credit enforcement for all journal types)
- PostingService (batch GL posting, period close, balance check)
- PayablesService (payment allocation, AP aging)
- InventoryService (FIFO cost, average cost, stock on hand)
- PurchasingService (PO > GR > Invoice > Return chain)
- Auth, Sync, Formatting, Validation

## Design Decisions

| Decision | Rationale |
|----------|-----------|
| **C# over Python** | Python 3.9+ dropped Win7 support; C# has massive AI training data for productivity |
| **SQLite over PostgreSQL** | Each register works offline; no network dependency for transactions |
| **INTEGER money (x100)** | No floating-point errors in financial calculations |
| **WinForms over WPF** | Simpler, lighter, runs on Win7 without GPU acceleration |
| **Sync via JSON files** | SQLite over SMB is officially unsupported; JSON batches are safe and auditable |
| **HMAC-SHA256 signing** | Prevents tampered sync batches from corrupting register data |
| **No CASCADE DELETE** | Financial data must never be accidentally deleted; all deletes are soft (control=3) |

## License

[MIT License](LICENSE) - Panandika

## Author

**Panandika** - [github.com/Panandika](https://github.com/Panandika)
