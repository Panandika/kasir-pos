# Kasir POS

[![Build and Test](https://github.com/Panandika/kasir-pos/actions/workflows/build.yml/badge.svg)](https://github.com/Panandika/kasir-pos/actions)
![.NET 10](https://img.shields.io/badge/.NET-10-purple)
![C# 12](https://img.shields.io/badge/C%23-12-blue)
![Avalonia](https://img.shields.io/badge/UI-Avalonia_12-orange)
![SQLite](https://img.shields.io/badge/SQLite-WAL_mode-green)
![License: MIT](https://img.shields.io/badge/License-MIT-yellow)

A full-featured Point of Sale system built to replace a **30-year-old FoxPro/Harbour retail system** running on Windows 7 register hardware. Handles 24,000+ products, 750+ vendors, 80,000+ AP records, and 3 registers with offline-first sync.

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
                    |  Avalonia UI    |
                    |  (34 Windows)   |
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
     |  (29 repos)      |
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
| C# | 12 | Application language |
| .NET | 10 | Runtime (cross-platform, self-contained publish) |
| Avalonia | 12.x | Cross-platform desktop UI (keyboard-driven, terminal theme) |
| SQLite | WAL mode | Local database per register |
| Microsoft.Data.Sqlite | 9.0.4 | SQLite driver with FTS5 + json1 |
| ESCPOS_NET | 2.0.0 | Cross-platform ESC/POS receipt printer |
| BCrypt.Net-Next | 4.1.0 | Password hashing (cost factor 10) |
| ClosedXML | 0.104.2 | Excel report export |
| Newtonsoft.Json | 13.0.4 | JSON serialization for sync |
| NUnit + FluentAssertions | 3.14 / 6.12 | Testing framework |
| GitHub Actions | - | CI: dotnet build on windows/macos/ubuntu |

## Project Structure

```
kasir-pos/
+-- Kasir.Core/                # Core library (business logic, data, hardware)
|   +-- Auth/                  # Authentication + role-based permissions
|   +-- Data/
|   |   +-- Repositories/      # 29 repository classes (CRUD)
|   |   +-- Schema.sql         # 57 tables, 28 triggers, 68 indexes
|   |   +-- DbConnection.cs    # Singleton, WAL mode, FTS5
|   +-- Hardware/              # ESC/POS printer, cash drawer, barcode scanner
|   +-- Models/                # 35 domain models
|   +-- Services/              # 13 business logic engines
|   +-- Sync/                  # Multi-register sync (Push/Pull/Engine)
|   +-- Utils/                 # Formatting, validation, clock
+-- Kasir.Avalonia/            # Avalonia UI project
|   +-- Forms/
|   |   +-- POS/               # Sale, Payment, Shift
|   |   +-- Master/            # Product, Vendor, Department, CreditCard, PriceChange
|   |   +-- Purchasing/        # PO, Goods Receipt, Invoice, Return
|   |   +-- Inventory/         # Stock Out, Transfer, Opname
|   |   +-- Accounting/        # Accounts, Journal, Payables, Cash, Posting
|   |   +-- Bank/              # Bank master, Giro processing
|   |   +-- Reports/           # Sales, Inventory, Financial reports
|   |   +-- Admin/             # Users, Printer, Backup, Update
|   |   +-- Shared/            # MsgBox, InputDialog, KeyboardRouter, ThemeConstants
+-- Kasir.Core.Tests/          # 247+ automated tests (NUnit)
+-- Kasir/                     # Legacy WinForms app (kept for reference)
+-- Kasir.Tests/               # Legacy WinForms tests
```

## Getting Started

### Prerequisites

- **.NET 10 SDK** — [download](https://dotnet.microsoft.com/download/dotnet/10.0)
- Works on **Windows 10/11**, **macOS**, **Linux**
- For receipt printing: thermal printer on a COM port or USB serial device

### Build

```bash
git clone https://github.com/Panandika/kasir-pos.git
cd kasir-pos
dotnet build Kasir.Avalonia.slnx
```

### Run

```bash
dotnet run --project Kasir.Avalonia/Kasir.Avalonia.csproj
```

On first run a setup dialog appears — choose **Seed** for a fresh database or **Import** to load an existing `kasir.db`.

### Run Tests

```bash
dotnet test Kasir.Core.Tests/Kasir.Core.Tests.csproj
```

### Deploy to Windows Register

```bash
dotnet publish Kasir.Avalonia/Kasir.Avalonia.csproj -c Release -r win-x64 --self-contained -o publish/
# Copy publish/ folder to register PC — no .NET install required
```

### Printer Configuration

Set the `printer_name` config key in the database to your device path:

| Platform | Example |
|----------|---------|
| Windows (serial) | `COM4` |
| Windows (parallel) | `LPT1` |
| macOS | `/dev/cu.usbserial-1234` |
| Linux | `/dev/ttyUSB0` or `/dev/usb/lp0` |

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

## Design Decisions

| Decision | Rationale |
|----------|-----------|
| **C# over Python** | Python 3.9+ dropped Win7 support; C# has massive AI training data for productivity |
| **SQLite over PostgreSQL** | Each register works offline; no network dependency for transactions |
| **INTEGER money (x100)** | No floating-point errors in financial calculations |
| **Avalonia over WinForms** | Cross-platform (Windows/macOS/Linux), .NET 10, self-contained publish |
| **AXAML + code-behind** | Matches the WinForms pattern — no MVVM framework needed |
| **Sync via JSON files** | SQLite over SMB is officially unsupported; JSON batches are safe and auditable |
| **HMAC-SHA256 signing** | Prevents tampered sync batches from corrupting register data |
| **No CASCADE DELETE** | Financial data must never be accidentally deleted; all deletes are soft (control=3) |

## License

[MIT License](LICENSE) - Panandika

## Author

**Panandika** - [github.com/Panandika](https://github.com/Panandika)
