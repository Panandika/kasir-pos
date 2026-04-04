-- ============================================================
-- RASIO/YONICO POS — SQLite Schema
-- Migrated from: SM/prg/CREATE.BAK (Harbour 3.0.0)
-- Data source:   main-kasir/MST, main-kasir/TRS (FoxPro 2.6)
--
-- Conventions:
--   - All monetary values stored as INTEGER (value × 100, i.e. Rupiah cents)
--   - All dates stored as TEXT in ISO 8601 format: 'YYYY-MM-DD'
--   - Period codes use 'YYYYMM' format (e.g. '202601' = January 2026)
--   - Document status lifecycle (control field):
--       0=draft, 1=normal, 2=printed/posted, 3=deleted, 4=edited, 5=replaced
--   - Each register has its own local database + sync process
-- ============================================================

-- ============================================================
-- Section 1: PRAGMA Configuration
-- ============================================================
PRAGMA journal_mode=WAL;
PRAGMA busy_timeout=5000;
PRAGMA synchronous=NORMAL;
PRAGMA foreign_keys=ON;
PRAGMA cache_size=-2000;
PRAGMA temp_store=MEMORY;

-- ============================================================
-- Section 2: System & Configuration Tables
-- ============================================================

-- Source: CONFIG.MEM (200+ variables), LOCAL.DBF, NETWORK.DBF
CREATE TABLE config (
    id          INTEGER PRIMARY KEY,
    key         TEXT    NOT NULL UNIQUE,
    value       TEXT,
    description TEXT
);

-- Source: COUNTER.DBF, NEXTNUM() function in SM
-- Each register gets unique prefix segment to avoid collisions
CREATE TABLE counters (
    id            INTEGER PRIMARY KEY,
    prefix        TEXT    NOT NULL,          -- e.g. 'KLR', 'MSK', 'BPB'
    register_id   TEXT    NOT NULL DEFAULT '01',
    current_value INTEGER NOT NULL DEFAULT 0,
    format        TEXT,                      -- e.g. '{prefix}-{REG}-{YYMM}-{SEQ:04d}'
    UNIQUE(prefix, register_id)
);

-- Source: LEVEL.DBF (main-kasir, 3 levels), PERMS array (SM)
CREATE TABLE roles (
    id          INTEGER PRIMARY KEY,
    name        TEXT    NOT NULL UNIQUE,     -- 'cashier', 'supervisor', 'admin'
    permissions TEXT                         -- JSON blob of permission flags
);

-- Source: PASS.DBF (main-kasir, 9 users), ASKPASS module (SM)
CREATE TABLE users (
    id            INTEGER PRIMARY KEY,
    username      TEXT    NOT NULL UNIQUE,
    password_hash TEXT    NOT NULL,          -- BCrypt.Net-Next (cost 12) or Rfc2898DeriveBytes (PBKDF2-SHA256, 260K iter)
    password_salt TEXT    NOT NULL,          -- RNGCryptoServiceProvider, hex-encoded (unused if BCrypt chosen — salt embedded in hash)
    display_name  TEXT    NOT NULL,
    alias         TEXT,                      -- 3-char alias for receipts (USRALIAS)
    role_id       INTEGER REFERENCES roles(id) ON DELETE RESTRICT,
    is_active     INTEGER NOT NULL DEFAULT 1,
    created_at    TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at    TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
);

-- Replaces period-suffixed files (acc_0126.dbf, msk_0126.dbf, etc.)
CREATE TABLE fiscal_periods (
    id          INTEGER PRIMARY KEY,
    period_code TEXT    NOT NULL UNIQUE,     -- 'YYYYMM' e.g. '202601'
    year        INTEGER NOT NULL,
    month       INTEGER NOT NULL,
    status      TEXT    NOT NULL DEFAULT 'O' CHECK(status IN ('O','C')),
    opened_at   TEXT,
    closed_at   TEXT
);

-- ============================================================
-- Section 3: Master Data Tables
-- ============================================================

-- Source: SM CREATE.BAK d_acc(), main-kasir MST/PERKIRA.DBF
-- Static reference data only — balances are in account_balances
CREATE TABLE accounts (
    id             INTEGER PRIMARY KEY,
    account_code   TEXT    NOT NULL UNIQUE,     -- KDAC C(15)
    account_name   TEXT    NOT NULL,            -- NMAC C(40)
    parent_code    TEXT,                        -- INDUK C(15) — self-referencing hierarchy
    is_detail      INTEGER NOT NULL DEFAULT 1,  -- DETIL: 1=detail(leaf), 0=header(parent)
    level          INTEGER NOT NULL DEFAULT 0,  -- LEVEL N(2)
    account_group  INTEGER NOT NULL DEFAULT 0,  -- GROUP N(1): 1=Asset,2=Liab,3=Equity,4=Rev,5=Exp
    normal_balance TEXT    NOT NULL DEFAULT 'D' CHECK(normal_balance IN ('D','K')),  -- DK
    verify_flag    TEXT    DEFAULT '',          -- VF C(1)
    changed_by     INTEGER,                    -- CHUSR
    changed_at     TEXT                        -- CHTIME/WAKTU
);

-- Per-period account balances — split from accounts to avoid 12x duplication/year
-- Source: SM d_acc() reset_acc(period) — awal/debet/credit fields
CREATE TABLE account_balances (
    id              INTEGER PRIMARY KEY,
    account_code    TEXT    NOT NULL REFERENCES accounts(account_code),
    period_code     TEXT    NOT NULL CHECK(period_code GLOB '??????'),  -- 'YYYYMM' exactly 6 chars
    opening_balance INTEGER NOT NULL DEFAULT 0, -- AWAL N(18,2) × 100
    debit_total     INTEGER NOT NULL DEFAULT 0, -- DEBET N(20,0) × 100
    credit_total    INTEGER NOT NULL DEFAULT 0, -- CREDIT N(20,0) × 100
    flag            TEXT    DEFAULT '',         -- FLAG C(1)
    UNIQUE(account_code, period_code)
);

-- Source: SM CREATE.BAK d_tab()
-- Maps inventory accounts to GL sales/COGS/return accounts
CREATE TABLE account_config (
    id              INTEGER PRIMARY KEY,
    account_code    TEXT    NOT NULL UNIQUE,    -- ACC C(15) — d_tab() primary index
    disc_pct        INTEGER DEFAULT 0,         -- DISC N(6,2) × 100
    point_value     INTEGER DEFAULT 0,         -- POINT N(9,0)
    date_start      TEXT,                      -- DATE1
    date_end        TEXT,                      -- DATE2
    value           INTEGER DEFAULT 0,         -- VAL N(15,2) × 100
    value_disc      INTEGER DEFAULT 0,         -- VALDISC N(15,2) × 100
    disc_x_pct      INTEGER DEFAULT 0,         -- DISCX N(5,2) × 100
    date_x_start    TEXT,                      -- DATEX1
    date_x_end      TEXT,                      -- DATEX2
    disc_y_pct      INTEGER DEFAULT 0,         -- DISCY N(5,2) × 100
    date_y_start    TEXT,                      -- DATEY1
    date_y_end      TEXT,                      -- DATEY2
    disc_z_pct      INTEGER DEFAULT 0,         -- DISCZ N(5,2) × 100
    date_z_start    TEXT,                      -- DATEZ1
    date_z_end      TEXT,                      -- DATEZ2
    sold_account    TEXT,                      -- ACCSOLD C(15)
    cogs_account    TEXT,                      -- ACCHPP C(15)
    return_account  TEXT,                      -- ACCRKL C(15)
    group_code      TEXT,                      -- GROUP C(1)
    rms_diff_acc    TEXT,                      -- SLSRMS C(15) — purchase return price diff
    rkl_diff_acc    TEXT,                      -- SLSRKL C(15) — sales return price diff
    trans_account   TEXT,                      -- ACCTRAN C(15)
    sales_code      TEXT,                      -- SALES C(15)
    changed_by      INTEGER,
    changed_at      TEXT
);

-- Source: SM CREATE.BAK d_sub(), main-kasir MST/VENDOR.DBF + CUSTOMER.DBF
-- Unified: vendors (group='1'), customers (group='2'), banks, locations, etc.
CREATE TABLE subsidiaries (
    id              INTEGER PRIMARY KEY,
    sub_code        TEXT    NOT NULL UNIQUE,    -- SUB C(15)
    name            TEXT    NOT NULL,           -- NAME C(40)
    account_code    TEXT,                       -- ACC C(15)
    contact_person  TEXT,                       -- PERSON C(30)
    credit_limit    INTEGER DEFAULT 0,         -- LIMIT N(17,2) × 100
    tax_name        TEXT,                       -- NAMENPWP C(60)
    tax_addr1       TEXT,                       -- ADR1NPWP C(60)
    tax_addr2       TEXT,                       -- ADR2NPWP C(60)
    address         TEXT,                       -- ADDRESS C(60)
    city            TEXT,                       -- CITY C(20)
    country         TEXT,                       -- COUNTRY C(15)
    npwp            TEXT,                       -- NPWP C(15) — tax ID
    remark          TEXT,                       -- REM C(60)
    remark2         TEXT,                       -- REM2 C(60)
    remark3         TEXT,                       -- REM3 C(60)
    max_value       INTEGER DEFAULT 0,         -- MAX N(17,2) × 100
    commission_pct  INTEGER DEFAULT 0,         -- KOMISI N(5,2) × 100
    last_balance    INTEGER DEFAULT 0,         -- LAST N(17,2) × 100
    total_in        INTEGER DEFAULT 0,         -- IN N(17,2) × 100
    total_out       INTEGER DEFAULT 0,         -- OUT N(17,2) × 100
    accum_account   TEXT,                      -- ACCUM C(15)
    group_code      TEXT    NOT NULL DEFAULT '1', -- GROUP C(1): 1=vendor,2=customer,3-5=other
    disc_account    TEXT,                       -- ACCDISC C(15)
    phone           TEXT,                       -- PHONE C(30)
    fax             TEXT,                       -- FAX C(30)
    giro_account    TEXT,                       -- ACCGIRO C(15)
    disc_pct        INTEGER DEFAULT 0,         -- DISC N(6,2) × 100
    cash_account    TEXT,                       -- ACCCASH C(15)
    alt_account     TEXT,                       -- ACC1 C(15)
    status          TEXT    DEFAULT 'A',        -- STATUS C(1)
    discount1       INTEGER DEFAULT 0,         -- DISCOUNT1 N(17,2) × 100
    disc1_pct       INTEGER DEFAULT 0,         -- DISC1 N(5,2) × 100
    disc2_pct       INTEGER DEFAULT 0,         -- DISC2 N(5,2) × 100
    bank_name       TEXT,                       -- BANK C(15)
    bank_holder     TEXT,                       -- ATASNAMA C(20)
    bank_account_no TEXT,                       -- REKNO C(15)
    bank_branch     TEXT,                       -- CABANG C(15)
    changed_by      INTEGER,
    changed_at      TEXT
);

-- Source: SM CREATE.BAK d_inv(), main-kasir MST/GOODS.DBF (24,457 records)
-- ALL fields from d_inv() included
CREATE TABLE products (
    id              INTEGER PRIMARY KEY,
    product_code    TEXT    NOT NULL UNIQUE,    -- INV C(20)
    account_code    TEXT,                       -- ACC C(15)
    type_sub        TEXT,                       -- JNSUB C(5)
    category_code   TEXT,                       -- CATEGORY C(5)
    dept_code       TEXT,                       -- DEPT C(2)
    barcode         TEXT,                       -- BARCODE C(15)
    name            TEXT    NOT NULL,           -- NAME C(75)
    status          TEXT    DEFAULT 'A' CHECK(status IN ('A','I','D')),
    unit1           TEXT,                       -- UNIT1 C(6) — purchase unit
    qty_min         INTEGER DEFAULT 0,         -- QMIN N(13,2) × 100
    qty_max         INTEGER DEFAULT 0,         -- QMAX N(13,2) × 100
    factor          INTEGER DEFAULT 1000,      -- FACTOR N(7,3) × 1000
    qty_order       INTEGER DEFAULT 0,         -- QORDER N(13,2) × 100
    location        TEXT,                       -- LOC C(6)
    unit            TEXT,                       -- UNIT C(6) — selling unit
    price           INTEGER DEFAULT 0,         -- PRICE N(13,2) × 100
    price1          INTEGER DEFAULT 0,         -- PRICE1 N(13,2) × 100 — wholesale tier 1
    price2          INTEGER DEFAULT 0,         -- PRICE2 N(13,2) × 100
    price3          INTEGER DEFAULT 0,         -- PRICE3 N(13,2) × 100
    price4          INTEGER DEFAULT 0,         -- PRICE4 N(13,2) × 100
    buying_price    INTEGER DEFAULT 0,         -- BUYING N(13,2) × 100
    vendor_code     TEXT,                       -- SUB C(15)
    qty_break2      INTEGER DEFAULT 0,         -- BATAS2 N(5,0) — qty threshold for price2
    qty_break3      INTEGER DEFAULT 0,         -- BATAS3 N(5,0) — qty threshold for price3
    unit2           TEXT,                       -- UNIT2 C(6) — alt unit
    conversion1     INTEGER DEFAULT 100,       -- KONVERSI1 N(6,2) × 100
    conversion2     INTEGER DEFAULT 100,       -- KONVERSI2 N(6,2) × 100
    is_consignment  TEXT    DEFAULT 'N',        -- KONSINYASI C(1) — affects COGS posting
    open_price      TEXT    DEFAULT 'N',        -- OPENPRICE C(1) — cashier can override price
    product_type    TEXT,                       -- JENIS C(15)
    disc_pct        INTEGER DEFAULT 0,         -- DISC N(6,2) × 100
    margin_pct      INTEGER DEFAULT 0,         -- PERSENTASE N(6,2) × 100
    profit          INTEGER DEFAULT 0,         -- PROFIT N(13,2) × 100
    cost_price      INTEGER DEFAULT 0,         -- HPOKOK N(13,2) × 100 — average COGS
    alt_vendor      TEXT,                       -- SUB1 C(15) — secondary vendor
    lowest_cost     INTEGER DEFAULT 0,         -- LOWHPP N(17,2) × 100
    luxury_tax_flag TEXT    DEFAULT 'N',        -- PPNBMS C(1)
    vat_flag        TEXT    DEFAULT 'N',        -- PPNS C(1)
    shelf_location  TEXT,                       -- RAK C(15)
    changed_by      INTEGER,
    changed_at      TEXT
);

-- Source: SM CREATE.BAK d_bcd()
-- One product can have multiple barcodes with qty multiplier and price override
CREATE TABLE product_barcodes (
    id              INTEGER PRIMARY KEY,
    product_code    TEXT    NOT NULL,           -- INV C(8) — links to products
    barcode         TEXT    NOT NULL UNIQUE,    -- derived from INV8+QBARCODE
    product_name    TEXT,                       -- NAME C(36) — denormalized for fast scan
    qty_per_scan    INTEGER NOT NULL DEFAULT 1, -- QBARCODE N(6,0) — e.g. box=12
    price_override  INTEGER,                    -- HARGA — if set, overrides product price
    customer_code   TEXT                        -- INVCUST C(15) — customer-specific code
);

-- Source: SM CREATE.BAK d_jns()
CREATE TABLE product_types (
    id          INTEGER PRIMARY KEY,
    type_code   TEXT    NOT NULL UNIQUE,        -- JENIS C(15)
    name        TEXT    NOT NULL,               -- NAME C(40)
    remark      TEXT,                           -- REM C(60)
    changed_by  INTEGER,
    changed_at  TEXT
);

-- Source: SM CREATE.BAK d_dep(), main-kasir MST/DEPT.DBF (194 records)
CREATE TABLE departments (
    id          INTEGER PRIMARY KEY,
    dept_code   TEXT    NOT NULL UNIQUE,        -- LOC C(6)
    name        TEXT    NOT NULL,               -- NAME C(40)
    changed_by  INTEGER,
    changed_at  TEXT
);

-- Source: SM CREATE.BAK d_loc()
CREATE TABLE locations (
    id             INTEGER PRIMARY KEY,
    location_code  TEXT    NOT NULL UNIQUE,     -- LOC1 C(3)
    name           TEXT    NOT NULL,            -- NAME C(40)
    remark         TEXT,                        -- REM C(60)
    changed_by     INTEGER,
    changed_at     TEXT
);

-- Source: SM CREATE.BAK d_crd()
CREATE TABLE credit_cards (
    id             INTEGER PRIMARY KEY,
    card_code      TEXT    NOT NULL UNIQUE,     -- CRD C(10)
    name           TEXT    NOT NULL,            -- NAME C(40)
    account_code   TEXT,                        -- ACC C(15) — GL account for card fees
    fee_pct        INTEGER DEFAULT 0,          -- DISC N(6,2) × 100 — card fee percentage
    min_value      INTEGER DEFAULT 0,          -- VAL N(15,2) × 100
    changed_by     INTEGER,
    changed_at     TEXT
);

-- Source: SM CREATE.BAK d_sec(), d_mbr()
CREATE TABLE members (
    id              INTEGER PRIMARY KEY,
    member_code     TEXT    NOT NULL UNIQUE,    -- SUB C(15)
    name            TEXT    NOT NULL,           -- NAME C(40)
    join_date       TEXT,                       -- DATE
    birthday        TEXT,                       -- BDAY
    status          TEXT    DEFAULT 'A',        -- STATUS C(1)
    opening_balance INTEGER DEFAULT 0,         -- AWAL N(18,2) × 100 — points
    address         TEXT,                       -- ADDRESS C(60)
    city            TEXT,                       -- CITY C(20)
    phone           TEXT,                       -- PHONE C(30)
    fax             TEXT,                       -- FAX C(30)
    remark          TEXT,                       -- REMARK C(30)
    religion        TEXT,                       -- RELIGION C(10)
    changed_by      INTEGER,
    changed_at      TEXT
);

-- Source: SM CREATE.BAK d_dis()
CREATE TABLE discounts (
    id             INTEGER PRIMARY KEY,
    product_code   TEXT    NOT NULL,            -- INV C(20)
    date_start     TEXT,                        -- DATE1
    date_end       TEXT,                        -- DATE2
    time_start     TEXT,                        -- TIME1 C(10)
    time_end       TEXT,                        -- TIME2 C(10)
    disc_pct       INTEGER DEFAULT 0,          -- DISC N(6,2) × 100
    disc1_pct      INTEGER DEFAULT 0,          -- DISC1 N(5,2) × 100
    disc2_pct      INTEGER DEFAULT 0,          -- DISC2 N(5,2) × 100
    disc3_pct      INTEGER DEFAULT 0,          -- DISC3 N(5,2) × 100
    disc_amount    INTEGER DEFAULT 0,          -- DISCRP N(7,0)
    value          INTEGER DEFAULT 0,          -- VAL N(15,2) × 100
    value1         INTEGER DEFAULT 0,          -- VAL1 N(15,2) × 100
    value2         INTEGER DEFAULT 0,          -- VAL2 N(15,2) × 100
    value3         INTEGER DEFAULT 0,          -- VAL3 N(15,2) × 100
    changed_by     INTEGER,
    changed_at     TEXT
);

-- Source: SM CREATE.BAK d_dsp()
-- Special discount rates per (account + subsidiary) pair
CREATE TABLE discount_partners (
    id             INTEGER PRIMARY KEY,
    account_code   TEXT    NOT NULL,            -- ACC C(15)
    sub_code       TEXT    NOT NULL,            -- SUB C(15)
    disc_pct       INTEGER DEFAULT 0,          -- DISC N(6,2) × 100
    changed_by     INTEGER,
    changed_at     TEXT,
    UNIQUE(account_code, sub_code)
);

-- Source: SM CREATE.BAK d_prz()
CREATE TABLE promotional_prices (
    id             INTEGER PRIMARY KEY,
    product_code   TEXT    NOT NULL,            -- INV C(20)
    date_start     TEXT,                        -- DATE1
    date_end       TEXT,                        -- DATE2
    time_start     TEXT,                        -- TIME1 C(10)
    time_end       TEXT,                        -- TIME2 C(10)
    promo_price    INTEGER NOT NULL,            -- VAL N(15,2) × 100
    journal_no     TEXT,                        -- NOJNL C(15) — linked sale
    cashier_code   TEXT,                        -- KASIR C(3)
    changed_by     INTEGER,
    changed_at     TEXT,
    created_at     TEXT                         -- CHTIME2
);

-- ============================================================
-- Section 4: Register/Accumulation Tables
-- ============================================================

-- Source: main-kasir TRS/GHIST.DBF (334,570 records) + TRS/GHISTB.DBF (110,967 archived)
-- Per-transaction inventory movement ledger — source for Kartu Persediaan reports.
-- Every stock-affecting event (purchase, sale, return, transfer, adjustment, opname)
-- appends one row here. GL batch posting reads this to accumulate period totals
-- into stock_register. Do NOT add FK on journal_no — it references documents
-- across multiple tables (sales, purchases, transfers, adjustments, etc.).
CREATE TABLE stock_movements (
    id              INTEGER PRIMARY KEY,
    product_code    TEXT    NOT NULL,           -- CODE C(20)
    vendor_code     TEXT    NOT NULL DEFAULT '', -- VENDID C(10)
    dept_code       TEXT    NOT NULL DEFAULT '', -- DEPTCD C(5)
    location_code   TEXT    NOT NULL DEFAULT '', -- LCODE C(5)
    account_code    TEXT    NOT NULL DEFAULT '', -- ACC C(15) — GL account
    sub_code        TEXT    NOT NULL DEFAULT '', -- SUB C(15)
    journal_no      TEXT    NOT NULL,           -- source document (BPB, KLR, RMS, OT, etc.)
    movement_type   TEXT    NOT NULL CHECK(movement_type IN (
                        'PURCHASE','SALE','RETURN_IN','RETURN_OUT',
                        'TRANSFER_IN','TRANSFER_OUT','ADJUSTMENT','OPNAME'
                    )),
    doc_date        TEXT    NOT NULL CHECK(doc_date GLOB '????-??-??'),
    period_code     TEXT    NOT NULL,           -- 'YYYYMM'
    qty_in          INTEGER NOT NULL DEFAULT 0, -- QIN N(13,2) × 100
    qty_out         INTEGER NOT NULL DEFAULT 0, -- QOUT N(13,2) × 100
    val_in          INTEGER NOT NULL DEFAULT 0, -- VIN N(17,0) × 100
    val_out         INTEGER NOT NULL DEFAULT 0, -- VOUT N(17,0) × 100
    cost_price      INTEGER NOT NULL DEFAULT 0, -- HPOKOK at time of movement × 100
    is_posted       INTEGER NOT NULL DEFAULT 0, -- 0=pending GL post, 1=posted
    is_archived     INTEGER NOT NULL DEFAULT 0, -- 1=imported from GHISTB
    changed_by      INTEGER,
    changed_at      TEXT,
    created_at      TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
);

-- Source: SM CREATE.BAK d_acr()
-- FIFO inventory running balances — updated by batch posting only
CREATE TABLE stock_register (
    id              INTEGER PRIMARY KEY,
    account_code    TEXT    NOT NULL,           -- ACC C(15)
    sub_code        TEXT    NOT NULL DEFAULT '', -- SUB C(15)
    product_code    TEXT    NOT NULL DEFAULT '', -- INV C(20)
    cost_price      INTEGER DEFAULT 0,         -- HPOKOK N(13,2) × 100
    qty_last        INTEGER DEFAULT 0,         -- QLAST N(13,2) × 100
    qty_final       INTEGER DEFAULT 0,         -- QAKHIR N(13,2) × 100
    qty_opname      INTEGER DEFAULT 0,         -- QSO N(13,2) × 100
    qty_sold        INTEGER DEFAULT 0,         -- QSEL N(13,2) × 100
    qty_in          INTEGER DEFAULT 0,         -- QIN N(13,2) × 100
    qty_out         INTEGER DEFAULT 0,         -- QOUT N(13,2) × 100
    roll_last       INTEGER DEFAULT 0,         -- ROLLAST N(15,0) × 100
    roll_in         INTEGER DEFAULT 0,         -- ROLIN N(15,0) × 100
    roll_out        INTEGER DEFAULT 0,         -- ROLOUT N(15,0) × 100
    val_last        INTEGER DEFAULT 0,         -- VLAST N(17,0) × 100
    val_in          INTEGER DEFAULT 0,         -- VIN N(17,0) × 100
    val_out         INTEGER DEFAULT 0,         -- VOUT N(17,0) × 100
    hpp             INTEGER DEFAULT 0,         -- HPP N(17,0) × 100
    period_code     TEXT    NOT NULL,           -- 'YYYYMM'
    last_posted_at  TEXT,                       -- posting freshness tracking
    UNIQUE(account_code, sub_code, product_code, period_code)
);

-- Source: SM CREATE.BAK d_rpt()
-- Loyalty points running balance per subsidiary per period
CREATE TABLE points_register (
    id              INTEGER PRIMARY KEY,
    sub_code        TEXT    NOT NULL,           -- SUB C(15)
    opening_balance INTEGER DEFAULT 0,         -- AWAL N(18,2) × 100
    debit_total     INTEGER DEFAULT 0,         -- DEBET (points earned)
    credit_total    INTEGER DEFAULT 0,         -- CREDIT (points redeemed)
    period_code     TEXT    NOT NULL,
    changed_by      INTEGER,
    changed_at      TEXT,
    UNIQUE(sub_code, period_code)
);

-- Source: SM CREATE.BAK d_acd()
-- General ledger posting detail entries
CREATE TABLE gl_details (
    id              INTEGER PRIMARY KEY,
    account_code    TEXT    NOT NULL,           -- ACC C(15)
    sub_code        TEXT    DEFAULT '',         -- SUB C(15)
    product_code    TEXT    DEFAULT '',         -- INV C(20)
    alt_sub         TEXT    DEFAULT '',         -- SUB1 C(15)
    alias           TEXT    DEFAULT '',         -- ALIAS C(3)
    journal_no      TEXT    NOT NULL,           -- NOJNL C(15)
    sales_code      TEXT    DEFAULT '',         -- SALES C(15)
    remark          TEXT,                       -- REMARK C(60)
    voucher_no      TEXT    DEFAULT '',         -- BUKTI C(15)
    ref             TEXT    DEFAULT '',         -- REF C(15)
    doc_date        TEXT    NOT NULL CHECK(doc_date GLOB '????-??-??'),
    debit           INTEGER DEFAULT 0,         -- DEBET N(20,0) × 100
    credit          INTEGER DEFAULT 0,         -- CREDIT N(20,0) × 100
    qty_in          INTEGER DEFAULT 0,         -- IN N(17,2) × 100
    qty_out         INTEGER DEFAULT 0,         -- OUT N(17,2) × 100
    period_code     TEXT    NOT NULL
);

-- Source: SM CREATE.BAK d_rhp(), main-kasir TRS/HUTANG.DBF (80,767 records)
-- AP/AR register
CREATE TABLE payables_register (
    id              INTEGER PRIMARY KEY,
    journal_no      TEXT    NOT NULL,           -- NOJNL C(15)
    doc_date        TEXT    NOT NULL CHECK(doc_date GLOB '????-??-??'),
    account_code    TEXT    NOT NULL,           -- ACC C(15)
    sub_code        TEXT    NOT NULL,           -- SUB C(15)
    sales_code      TEXT    DEFAULT '',         -- SALES C(15)
    ref             TEXT    DEFAULT '',         -- REF C(15)
    remark          TEXT,                       -- REMARK C(60)
    value           INTEGER NOT NULL DEFAULT 0, -- VAL N(15,2) × 100
    due_date        TEXT,                       -- DUEDATE
    warehouse       TEXT    DEFAULT '',         -- KMS C(15)
    direction       TEXT    NOT NULL DEFAULT 'D' CHECK(direction IN ('D','K')),
    is_posted       TEXT    DEFAULT 'N',        -- POSTED C(1)
    payment_amount  INTEGER DEFAULT 0,         -- PAYMENT N(17,2) × 100
    is_printed      INTEGER DEFAULT 0,         -- PRINTED L
    flag            TEXT    DEFAULT '',         -- FLAG C(1)
    control         INTEGER DEFAULT 1 CHECK(control IN (0,1,2,3,4,5)),
    print_count     INTEGER DEFAULT 0,         -- NPRINT N(2)
    gross_amount    INTEGER DEFAULT 0,         -- BRUTO N(17,2) × 100
    is_paid         TEXT    DEFAULT 'N',        -- BAYAR C(1)
    disc_amount     INTEGER DEFAULT 0,         -- DISC N(10,2) × 100
    period_code     TEXT    NOT NULL,
    changed_by      INTEGER,
    changed_at      TEXT
);

-- Source: SM CREATE.BAK d_rgr()
CREATE TABLE giro_register (
    id              INTEGER PRIMARY KEY,
    account_code    TEXT    NOT NULL,           -- ACC C(15)
    sub_code        TEXT    NOT NULL,           -- SUB C(15)
    giro_no         TEXT    NOT NULL,           -- BG C(15)
    giro_date       TEXT,                       -- BGDATE
    doc_date        TEXT    NOT NULL CHECK(doc_date GLOB '????-??-??'),
    journal_no      TEXT,                       -- JNL C(15)
    value           INTEGER NOT NULL DEFAULT 0, -- VAL N(15,2) × 100
    remark          TEXT,                       -- REMARK C(60)
    direction       TEXT    NOT NULL CHECK(direction IN ('D','K')),
    status          TEXT    DEFAULT 'O',        -- STATUS C(1): O=open, C=cleared
    approved_by     INTEGER,                   -- USROK
    control         INTEGER DEFAULT 1 CHECK(control IN (0,1,2,3,4,5)),
    print_count     INTEGER DEFAULT 0,
    period_code     TEXT    NOT NULL,
    changed_by      INTEGER,
    changed_at      TEXT
);

-- Source: SM CREATE.BAK d_ror()
CREATE TABLE order_register (
    id              INTEGER PRIMARY KEY,
    journal_no      TEXT    NOT NULL,           -- JNL C(15)
    product_code    TEXT,                       -- INV C(20)
    sub_code        TEXT,                       -- SUB C(15)
    alt_sub         TEXT,                       -- SUB1 C(15)
    group_code      TEXT,                       -- GROUP C(1)
    roll_in         INTEGER DEFAULT 0,         -- ROLIN N(15,0) × 100
    vat_flag        TEXT    DEFAULT 'N',        -- PPN C(1)
    pph_pct         INTEGER DEFAULT 0,         -- PPH N(5,2) × 100
    remark          TEXT,                       -- REMARK C(60)
    qty_in          INTEGER DEFAULT 0,         -- QIN N(13,2) × 100
    qty_out         INTEGER DEFAULT 0,         -- QOUT N(13,2) × 100
    value           INTEGER DEFAULT 0,         -- VAL N(15,2) × 100
    account_code    TEXT,                       -- ACC C(15)
    roll_out        INTEGER DEFAULT 0,         -- ROLOUT N(15,0) × 100
    doc_date        TEXT,
    due_date        TEXT,
    direction       TEXT    CHECK(direction IN ('D','K')),
    period_code     TEXT    NOT NULL,
    register_id     TEXT    DEFAULT '01',
    legacy_source   TEXT    DEFAULT 'CS',
    changed_by      INTEGER,
    changed_at      TEXT
);

-- ============================================================
-- Section 5: Transaction Tables (header + detail pairs)
-- ============================================================

-- ----- SALES (KLR/KLD + RKL/RKD) -----

-- Source: SM CREATE.BAK d_klr() + d_rkl()
CREATE TABLE sales (
    id              INTEGER PRIMARY KEY,
    doc_type        TEXT    NOT NULL CHECK(doc_type IN ('SALE','SALE_RETURN')),
    journal_no      TEXT    NOT NULL UNIQUE,    -- NOJNL C(15)
    doc_date        TEXT    NOT NULL CHECK(doc_date GLOB '????-??-??'),
    account_code    TEXT    DEFAULT '',         -- ACC C(15)
    sub_code        TEXT    DEFAULT '',         -- SUB C(15) — customer
    member_code     TEXT    DEFAULT '',         -- SEC C(5) — member card
    point_value     INTEGER DEFAULT 0,         -- POINT N(8,2) × 100
    card_code       TEXT    DEFAULT '',         -- CARD C(15)
    group1          TEXT    DEFAULT '',         -- GROUP1 C(1)
    ap_journal      TEXT    DEFAULT '',         -- JNLRHP C(15) — linked AP/AR
    tax_invoice     TEXT    DEFAULT '',         -- FP C(8)
    tax_inv_date    TEXT,                       -- DATEFP
    ref_no          TEXT    DEFAULT '',         -- NOREF C(15)
    remark          TEXT,                       -- REMARK C(60)
    sales_code      TEXT    DEFAULT '',         -- SALES C(15)
    due_date        TEXT,                       -- DUEDATE
    cashier         TEXT    DEFAULT '',         -- KASIR C(15)
    disc_pct        INTEGER DEFAULT 0,         -- DISC N(6,2) × 100
    disc2_pct       INTEGER DEFAULT 0,         -- DISC2 N(15,0) × 100
    warehouse       TEXT    DEFAULT '',         -- KMS C(15)
    shift           TEXT    DEFAULT '1',        -- SHIFT C(1)
    payment_amount  INTEGER DEFAULT 0,         -- PAYMENT N(15,2) × 100
    vat_flag        TEXT    DEFAULT 'N',        -- PPN C(1)
    cash_amount     INTEGER DEFAULT 0,         -- CASH N(17,0) × 100
    non_cash        INTEGER DEFAULT 0,         -- NON N(15,2) × 100
    total_value     INTEGER DEFAULT 0,         -- VAL N(15,2) × 100
    vat_amount      INTEGER DEFAULT 0,         -- PPN2 N(17,2) × 100
    change_amount   INTEGER DEFAULT 0,         -- CHANGE N(17,0) × 100
    total_disc      INTEGER DEFAULT 0,         -- TDISC N(17,0) × 100
    card_type       TEXT    DEFAULT '',         -- J_CARD C(1): D=debit, C=credit
    gross_amount    INTEGER DEFAULT 0,         -- BRUTO N(17,2) × 100
    voucher_amount  INTEGER DEFAULT 0,         -- VOUCHER N(17,0) × 100
    credit_amount   INTEGER DEFAULT 0,         -- CREDIT N(20,0) × 100
    is_posted       TEXT    DEFAULT 'N',        -- POSTED C(1)
    is_paid         TEXT    DEFAULT 'N',        -- BAYAR C(1)
    group_code      TEXT    DEFAULT '',         -- GROUP C(1)
    cc_account      TEXT    DEFAULT '',         -- ACC_CC C(10)
    cc_number       TEXT    DEFAULT '',         -- NO_CC C(20)
    ref2            TEXT    DEFAULT '',         -- REF C(15)
    alt_sub         TEXT    DEFAULT '',         -- SUB2 C(15)
    commission_pct  INTEGER DEFAULT 0,         -- KOMISI N(5,2) × 100
    control         INTEGER DEFAULT 1 CHECK(control IN (0,1,2,3,4,5)),
    print_count     INTEGER DEFAULT 0,         -- NPRINT N(2)
    is_printed      INTEGER DEFAULT 0,         -- PRINTED L
    approved_by     INTEGER,                   -- USROK
    period_code     TEXT    NOT NULL,
    register_id     TEXT    DEFAULT '01',       -- which register created this
    legacy_source   TEXT    DEFAULT 'CS',       -- 'SM'=legacy, 'CS'=C# app
    changed_by      INTEGER,
    changed_at      TEXT
);

-- Source: SM CREATE.BAK d_kld() + d_rkd()
CREATE TABLE sale_items (
    id              INTEGER PRIMARY KEY,
    journal_no      TEXT    NOT NULL REFERENCES sales(journal_no) ON DELETE RESTRICT,
    order_ref       TEXT    DEFAULT '',         -- OKL C(15) — linked order
    account_code    TEXT    DEFAULT '',         -- ACC C(15)
    sub_code        TEXT    DEFAULT '',         -- SUB C(15)
    product_code    TEXT    NOT NULL,           -- INV C(20)
    remark          TEXT,                       -- REMARK C(30)
    quantity        INTEGER NOT NULL DEFAULT 0, -- QTY N(13,2) × 100
    qty_box         INTEGER DEFAULT 0,         -- QDUS N(8,2) × 100
    value           INTEGER NOT NULL DEFAULT 0, -- VAL N(15,2) × 100
    cogs            INTEGER DEFAULT 0,         -- HPP N(17,0) × 100
    group_code      TEXT    DEFAULT '',         -- GROUP C(1)
    disc_pct        INTEGER DEFAULT 0,         -- DISC N(6,2) × 100
    unit_price      INTEGER DEFAULT 0,         -- PRICE N(13,2) × 100
    inv_price       INTEGER DEFAULT 0,         -- PRICEINV N(9,0)
    point_value     INTEGER DEFAULT 0,         -- POINT N(8,2) × 100
    qty_order       INTEGER DEFAULT 0,         -- QORDER N(13,2) × 100
    disc_value      INTEGER DEFAULT 0          -- DISCV N(17,2) × 100
);

-- ----- PURCHASES (MSK/MSD + BPB/BPD + RMS/RMD) -----

-- Source: SM CREATE.BAK d_msk() + d_bpb() + d_rms()
CREATE TABLE purchases (
    id              INTEGER PRIMARY KEY,
    doc_type        TEXT    NOT NULL CHECK(doc_type IN ('PURCHASE','RECEIPT','PURCHASE_RETURN')),
    journal_no      TEXT    NOT NULL UNIQUE,
    doc_date        TEXT    NOT NULL CHECK(doc_date GLOB '????-??-??'),
    account_code    TEXT    DEFAULT '',
    sub_code        TEXT    NOT NULL,           -- vendor
    group1          TEXT    DEFAULT '',         -- GROUP1 C(1) — MSK/RMS only
    tax_invoice1    TEXT    DEFAULT '',         -- FP1 C(8) — MSK only
    tax_invoice     TEXT    DEFAULT '',         -- FP C(8)
    tax_inv_date    TEXT,                       -- DATEFP
    ref_no          TEXT    DEFAULT '',         -- NOREF C(15)
    remark          TEXT,
    sales_code      TEXT    DEFAULT '',         -- SALES C(15) — MSK only
    due_date        TEXT,
    disc_pct        INTEGER DEFAULT 0,
    disc2           INTEGER DEFAULT 0,         -- DISC2 N(15,0) × 100 — MSK only
    warehouse       TEXT    DEFAULT '',         -- KMS C(15)
    commission_pct  INTEGER DEFAULT 0,         -- KOMISI N(5,2) × 100
    vat_flag        TEXT    DEFAULT 'N',
    total_value     INTEGER DEFAULT 0,
    vat_amount      INTEGER DEFAULT 0,         -- PPN2 N(17,2) × 100
    delivery_note   TEXT    DEFAULT '',         -- SJL C(15) — MSK only
    ref2            TEXT    DEFAULT '',         -- REF C(15)
    alt_sub         TEXT    DEFAULT '',         -- SUB2 C(15)
    total_disc      INTEGER DEFAULT 0,         -- TDISC N(17,0) × 100
    expedition      TEXT    DEFAULT '',         -- EXPED C(15) — MSK only
    packaging       TEXT    DEFAULT '',         -- PACK C(15) — MSK only
    doc_subtype     TEXT    DEFAULT '',         -- JNS C(1)
    gross_amount    INTEGER DEFAULT 0,
    is_posted       TEXT    DEFAULT 'N',
    is_paid         TEXT    DEFAULT 'N',        -- BAYAR C(1) — MSK only
    group_code      TEXT    DEFAULT '',
    pph_pct         INTEGER DEFAULT 0,         -- PPH N(5,2) × 100 — BPB only
    val1            INTEGER DEFAULT 0,         -- VAL1 N(15,2) × 100 — BPB only
    control         INTEGER DEFAULT 1 CHECK(control IN (0,1,2,3,4,5)),
    print_count     INTEGER DEFAULT 0,
    is_printed      INTEGER DEFAULT 0,
    approved_by     INTEGER,
    period_code     TEXT    NOT NULL,
    register_id     TEXT    DEFAULT '01',
    legacy_source   TEXT    DEFAULT 'CS',
    changed_by      INTEGER,
    changed_at      TEXT
);

-- Source: SM CREATE.BAK d_msd() + d_bpd() + d_rmd()
CREATE TABLE purchase_items (
    id              INTEGER PRIMARY KEY,
    journal_no      TEXT    NOT NULL REFERENCES purchases(journal_no) ON DELETE RESTRICT,
    order_ref       TEXT    DEFAULT '',         -- OKL/OMS C(15)
    account_code    TEXT    DEFAULT '',
    sub_code        TEXT    DEFAULT '',
    product_code    TEXT    NOT NULL,
    customer_code   TEXT    DEFAULT '',         -- INVCUST C(15)
    remark          TEXT,
    qty1            INTEGER DEFAULT 0,         -- Q1 N(13,2) × 100 — MSD only
    qty2            INTEGER DEFAULT 0,         -- Q2 N(13,2) × 100 — MSD only
    quantity        INTEGER NOT NULL DEFAULT 0,
    value           INTEGER NOT NULL DEFAULT 0,
    cogs            INTEGER DEFAULT 0,         -- HPP N(17,0) × 100
    group_code      TEXT    DEFAULT '',
    disc_amount     INTEGER DEFAULT 0,         -- DISCRP N(7,0)
    disc_pct        INTEGER DEFAULT 0,
    disc2_pct       INTEGER DEFAULT 0,         -- DISCD2 N(5,2) × 100 — MSD only
    unit_price      INTEGER DEFAULT 0,
    inv_price       INTEGER DEFAULT 0,         -- PRICEINV N(9,0) — MSD only
    qty_order       INTEGER DEFAULT 0,         -- QORDER N(13,2) × 100 — MSD only
    disc_value      INTEGER DEFAULT 0,         -- DISCV N(17,2) × 100 — MSD only
    roll            INTEGER DEFAULT 0          -- ROL N(15,0) × 100 — RMD/BPD
);

-- ----- CASH TRANSACTIONS (KMS/KMD + KKL/KKD + BMS/BMD + BKL/BKD) -----

-- Source: SM CREATE.BAK d_kms() + d_kkl() + d_bms() + d_bkl()
CREATE TABLE cash_transactions (
    id              INTEGER PRIMARY KEY,
    doc_type        TEXT    NOT NULL CHECK(doc_type IN ('CASH_IN','CASH_OUT','BANK_IN','BANK_OUT')),
    journal_no      TEXT    NOT NULL UNIQUE,
    doc_date        TEXT    NOT NULL CHECK(doc_date GLOB '????-??-??'),
    sub_code        TEXT    DEFAULT '',         -- SUB C(15) — KMS only
    ref             TEXT    DEFAULT '',
    remark          TEXT,
    total_value     INTEGER DEFAULT 0,
    is_posted       TEXT    DEFAULT 'N',
    group_code      TEXT    DEFAULT '',         -- GROUP C(1)
    description     TEXT    DEFAULT '',         -- KET C(50)
    control         INTEGER DEFAULT 1 CHECK(control IN (0,1,2,3,4,5)),
    print_count     INTEGER DEFAULT 0,
    approved_by     INTEGER,
    period_code     TEXT    NOT NULL,
    register_id     TEXT    DEFAULT '01',
    legacy_source   TEXT    DEFAULT 'CS',
    changed_by      INTEGER,
    changed_at      TEXT
);

-- Source: SM CREATE.BAK d_kmd() + d_kkd() + d_bmd() + d_bkd()
CREATE TABLE cash_transaction_lines (
    id              INTEGER PRIMARY KEY,
    journal_no      TEXT    NOT NULL REFERENCES cash_transactions(journal_no) ON DELETE RESTRICT,
    sub_code        TEXT    DEFAULT '',
    account_code    TEXT    NOT NULL,
    ref_no          TEXT    DEFAULT '',         -- NOREF C(15)
    remark          TEXT,
    giro_no         TEXT    DEFAULT '',         -- BG C(15)
    giro_date       TEXT,                       -- BGDATE
    giro_status     TEXT    DEFAULT '',         -- STATUS C(1)
    direction       TEXT    NOT NULL CHECK(direction IN ('D','K')),
    value           INTEGER NOT NULL DEFAULT 0,
    link_journal    TEXT    DEFAULT ''          -- KLR/MSK reference — BMD/BKD only
);

-- ----- MEMORIAL JOURNALS (UMH/UMD + PTH/PTD) -----

-- Source: SM CREATE.BAK d_umh() + d_pth()
CREATE TABLE memorial_journals (
    id              INTEGER PRIMARY KEY,
    doc_type        TEXT    NOT NULL CHECK(doc_type IN ('MEMORIAL','POINTS')),
    journal_no      TEXT    NOT NULL UNIQUE,
    doc_date        TEXT    NOT NULL CHECK(doc_date GLOB '????-??-??'),
    ref             TEXT    DEFAULT '',
    ref_no          TEXT    DEFAULT '',         -- NOREF C(15) — UMH only
    remark          TEXT,
    group_code      TEXT    DEFAULT '',
    control         INTEGER DEFAULT 1 CHECK(control IN (0,1,2,3,4,5)),
    print_count     INTEGER DEFAULT 0,
    approved_by     INTEGER,
    period_code     TEXT    NOT NULL,
    register_id     TEXT    DEFAULT '01',
    legacy_source   TEXT    DEFAULT 'CS',
    changed_by      INTEGER,
    changed_at      TEXT
);

-- Source: SM CREATE.BAK d_umd() + d_ptd()
CREATE TABLE memorial_journal_lines (
    id              INTEGER PRIMARY KEY,
    journal_no      TEXT    NOT NULL REFERENCES memorial_journals(journal_no) ON DELETE RESTRICT,
    sub_code        TEXT    DEFAULT '',
    account_code    TEXT    DEFAULT '',         -- UMD only
    product_code    TEXT    DEFAULT '',
    alt_sub         TEXT    DEFAULT '',         -- SUBK C(15)
    remark          TEXT,
    name            TEXT    DEFAULT '',         -- PTD only: member name
    quantity        INTEGER DEFAULT 0,
    unit_price      INTEGER DEFAULT 0,         -- UMD only
    direction       TEXT    CHECK(direction IN ('D','K')),
    value           INTEGER DEFAULT 0,
    group_code      TEXT    DEFAULT '',
    roll            INTEGER DEFAULT 0,         -- ROL N(15,0) × 100 — UMD only
    sticker_count   INTEGER DEFAULT 0,         -- STIKER N(5,0) — PTD only
    max_sticker     INTEGER DEFAULT 0          -- MAX N(5,0) — PTD only
);

-- ----- ORDERS (OMS/OMD + OKL/OKD) -----

-- Source: SM CREATE.BAK d_oms() + d_okl()
CREATE TABLE orders (
    id              INTEGER PRIMARY KEY,
    doc_type        TEXT    NOT NULL CHECK(doc_type IN ('PURCHASE_ORDER','SALES_ORDER')),
    journal_no      TEXT    NOT NULL UNIQUE,
    doc_date        TEXT    NOT NULL CHECK(doc_date GLOB '????-??-??'),
    account_code    TEXT    DEFAULT '',         -- OMS only
    sub_code        TEXT    NOT NULL,
    group1          TEXT    DEFAULT '',         -- OMS only
    remark          TEXT,
    total_value     INTEGER DEFAULT 0,
    disc_pct        INTEGER DEFAULT 0,
    due_date        TEXT,
    is_posted       TEXT    DEFAULT 'N',
    vat_flag        TEXT    DEFAULT 'N',        -- OMS only
    luxury_tax_pct  INTEGER DEFAULT 0,         -- PPNBM — OMS only
    pph_pct         INTEGER DEFAULT 0,         -- PPH — OMS only
    val1            INTEGER DEFAULT 0,         -- OMS only
    sales_code      TEXT    DEFAULT '',         -- SALES — OKL only
    order_seq       INTEGER DEFAULT 0,         -- ORDKE — OKL only
    control         INTEGER DEFAULT 1 CHECK(control IN (0,1,2,3,4,5)),
    print_count     INTEGER DEFAULT 0,
    approved_by     INTEGER,
    period_code     TEXT    NOT NULL,
    register_id     TEXT    DEFAULT '01',
    legacy_source   TEXT    DEFAULT 'CS',
    changed_by      INTEGER,
    changed_at      TEXT
);

-- Source: SM CREATE.BAK d_omd() + d_okd()
CREATE TABLE order_items (
    id              INTEGER PRIMARY KEY,
    journal_no      TEXT    NOT NULL REFERENCES orders(journal_no) ON DELETE RESTRICT,
    account_code    TEXT    DEFAULT '',
    sub_code        TEXT    DEFAULT '',
    product_code    TEXT    NOT NULL,
    group_code      TEXT    DEFAULT '',
    remark          TEXT,
    quantity        INTEGER NOT NULL DEFAULT 0,
    value           INTEGER DEFAULT 0,
    disc_pct        INTEGER DEFAULT 0,
    unit_price      INTEGER DEFAULT 0,
    roll            INTEGER DEFAULT 0,         -- ROL — OMD only
    disc_value      INTEGER DEFAULT 0          -- DISCV — OKD only
);

-- ----- STOCK TRANSFERS (TRM/TRD + UNH/UND) -----

-- Source: SM CREATE.BAK d_trm() + d_unh()
CREATE TABLE stock_transfers (
    id              INTEGER PRIMARY KEY,
    doc_type        TEXT    NOT NULL CHECK(doc_type IN ('TRANSFER','WAREHOUSE_MEMO')),
    journal_no      TEXT    NOT NULL UNIQUE,
    doc_date        TEXT    NOT NULL CHECK(doc_date GLOB '????-??-??'),
    dest_account    TEXT    DEFAULT '',         -- ACCK C(15)
    dest_sub        TEXT    DEFAULT '',         -- SUBK C(15)
    src_account     TEXT    DEFAULT '',         -- ACCD C(15)
    src_sub         TEXT    DEFAULT '',         -- SUBD C(15)
    ref             TEXT    DEFAULT '',
    remark          TEXT,
    control         INTEGER DEFAULT 1 CHECK(control IN (0,1,2,3,4,5)),
    print_count     INTEGER DEFAULT 0,
    period_code     TEXT    NOT NULL,
    register_id     TEXT    DEFAULT '01',
    legacy_source   TEXT    DEFAULT 'CS',
    changed_by      INTEGER,
    changed_at      TEXT
);

-- Source: SM CREATE.BAK d_trd() + d_und()
CREATE TABLE stock_transfer_items (
    id              INTEGER PRIMARY KEY,
    journal_no      TEXT    NOT NULL REFERENCES stock_transfers(journal_no) ON DELETE RESTRICT,
    product_code    TEXT    NOT NULL,
    account_code    TEXT    DEFAULT '',         -- TRD only
    remark          TEXT,
    quantity        INTEGER NOT NULL DEFAULT 0,
    unit_price      INTEGER DEFAULT 0,
    value           INTEGER DEFAULT 0,
    group_code      TEXT    DEFAULT ''
);

-- ----- STOCK ADJUSTMENTS (OTHDR/OTDTL) -----

-- Source: main-kasir TRS/OTHDR.DBF (438 records) + OTDTL.DBF (2,150 records)
-- Covers: Pemakaian (internal use), Rusak (damaged), Hilang (lost/shrinkage)
-- These are unilateral stock-out write-offs — stock leaves the system entirely.
-- GL effect: DR expense account / CR inventory account (via batch posting).
-- Not a transfer (no destination). Not a general journal (has product qty).
CREATE TABLE stock_adjustments (
    id              INTEGER PRIMARY KEY,
    doc_type        TEXT    NOT NULL CHECK(doc_type IN ('USAGE','DAMAGE','LOSS')),
    journal_no      TEXT    NOT NULL UNIQUE,    -- REFNO C(15) from OTHDR
    doc_date        TEXT    NOT NULL CHECK(doc_date GLOB '????-??-??'),
    location_code   TEXT    NOT NULL DEFAULT '', -- LCODE C(3) — FK to locations
    remark          TEXT,
    total_value     INTEGER DEFAULT 0,          -- sum of line values × 100
    is_posted       TEXT    DEFAULT 'N',
    control         INTEGER DEFAULT 1 CHECK(control IN (0,1,2,3,4,5)),
    print_count     INTEGER DEFAULT 0,
    approved_by     INTEGER,
    period_code     TEXT    NOT NULL,
    register_id     TEXT    DEFAULT '01',
    legacy_source   TEXT    DEFAULT 'CS',
    changed_by      INTEGER,
    changed_at      TEXT
);

-- Source: OTDTL.DBF — line items for stock adjustments
CREATE TABLE stock_adjustment_items (
    id              INTEGER PRIMARY KEY,
    journal_no      TEXT    NOT NULL REFERENCES stock_adjustments(journal_no) ON DELETE RESTRICT,
    product_code    TEXT    NOT NULL,           -- CODE C(15) from OTDTL
    account_code    TEXT    DEFAULT '',         -- inventory GL account
    remark          TEXT,
    quantity        INTEGER NOT NULL DEFAULT 0, -- qty × 100
    unit_price      INTEGER DEFAULT 0,          -- cost price × 100
    value           INTEGER DEFAULT 0           -- quantity × unit_price / 100
);

-- ----- PENDING SALES -----

-- Source: SM CREATE.BAK d_pen() — hold/suspend POS transactions
CREATE TABLE pending_sales (
    id              INTEGER PRIMARY KEY,
    journal_no      TEXT    NOT NULL,           -- NOJNL C(15)
    product_code    TEXT    NOT NULL,           -- INV C(20)
    sub_flag        TEXT    DEFAULT '',         -- SUB C(1)
    disc_pct        INTEGER DEFAULT 0,         -- DISC N(10,2) × 100
    account_code    TEXT    DEFAULT '',         -- ACC C(15)
    remark          TEXT,                       -- REM C(36)
    quantity        INTEGER NOT NULL DEFAULT 0, -- QTY N(9,3) × 1000
    unit_price      INTEGER DEFAULT 0,         -- PRC N(7,0)
    cogs            INTEGER DEFAULT 0,         -- HPP N(17,0) × 100
    value           INTEGER DEFAULT 0,         -- VAL N(12,2) × 100
    inv_price       INTEGER DEFAULT 0,         -- INVPRC
    is_consignment  TEXT    DEFAULT 'N'        -- KONSINYASI C(1)
);

-- ----- STOCK OPNAME -----

-- Source: SM CREATE.BAK d_opn()
CREATE TABLE stock_opname (
    id              INTEGER PRIMARY KEY,
    doc_date        TEXT    NOT NULL CHECK(doc_date GLOB '????-??-??'),
    account_code    TEXT    NOT NULL,           -- ACC C(15)
    dest_account    TEXT    DEFAULT '',         -- ACCK C(15)
    sub_code        TEXT    DEFAULT '',         -- SUB C(15)
    product_code    TEXT    NOT NULL,           -- INV C(20)
    unit_flag       TEXT    DEFAULT '',         -- UNITG C(1)
    qty_system      INTEGER DEFAULT 0,         -- QLAST N(13,2) × 100
    cost_price      INTEGER DEFAULT 0,         -- HPP N(17,0) × 100
    dept_code       TEXT    DEFAULT '',         -- DEPT C(2)
    product_name    TEXT,                       -- NAME C(75) — denormalized for report
    qty_opname      INTEGER DEFAULT 0,         -- OQTY N(13,2) × 100
    qty_actual      INTEGER DEFAULT 0,         -- QTY N(13,2) × 100
    qty1            INTEGER DEFAULT 0,         -- Q1 N(13,2) × 100
    qty2            INTEGER DEFAULT 0,         -- Q2 N(13,2) × 100
    category_code   TEXT    DEFAULT '',         -- CATEGORY C(5)
    alt_sub         TEXT    DEFAULT '',         -- SUB2 C(15)
    period_code     TEXT    NOT NULL,
    changed_by      INTEGER,
    changed_at      TEXT
);

-- ============================================================
-- Section 6: Specialized/Deferred Tables
-- ============================================================

-- Source: SM CREATE.BAK d_opr(), main-kasir MST/GPRICE.DBF (76,216 records)
CREATE TABLE price_history (
    id              INTEGER PRIMARY KEY,
    product_code    TEXT    NOT NULL,           -- INV C(20)
    product_name    TEXT,                       -- NAME C(80) — denormalized
    doc_date        TEXT    NOT NULL,
    old_date        TEXT,                       -- ODATE
    quantity        INTEGER DEFAULT 0,
    old_quantity    INTEGER DEFAULT 0,         -- OQTY
    journal_no      TEXT    DEFAULT '',
    old_journal_no  TEXT    DEFAULT '',         -- ONOJNL
    sub_code        TEXT    DEFAULT '',
    group_code      TEXT    DEFAULT '',
    value           INTEGER DEFAULT 0,
    old_value       INTEGER DEFAULT 0,         -- OVAL
    disc_remark     TEXT    DEFAULT '',         -- REMDISC C(10)
    old_disc_remark TEXT    DEFAULT '',         -- REMODISC C(10)
    vat_flag        TEXT    DEFAULT 'N',
    old_vat_flag    TEXT    DEFAULT 'N',        -- OPPN
    period_code     TEXT    NOT NULL
);

-- Source: SM CREATE.BAK d_bud()
CREATE TABLE budget (
    id              INTEGER PRIMARY KEY,
    account_code    TEXT    NOT NULL,           -- ACC C(15)
    sub_code        TEXT    DEFAULT '',         -- SUB C(15)
    month_01        INTEGER DEFAULT 0,         -- BUL01 N(15,2) × 100
    month_02        INTEGER DEFAULT 0,
    month_03        INTEGER DEFAULT 0,
    month_04        INTEGER DEFAULT 0,
    month_05        INTEGER DEFAULT 0,
    month_06        INTEGER DEFAULT 0,
    month_07        INTEGER DEFAULT 0,
    month_08        INTEGER DEFAULT 0,
    month_09        INTEGER DEFAULT 0,
    month_10        INTEGER DEFAULT 0,
    month_11        INTEGER DEFAULT 0,
    month_12        INTEGER DEFAULT 0,
    fiscal_year     INTEGER NOT NULL,
    changed_by      INTEGER,
    changed_at      TEXT,
    UNIQUE(account_code, sub_code, fiscal_year)
);

-- Source: SM CREATE.BAK d_fpj()
CREATE TABLE tax_invoices (
    id                INTEGER PRIMARY KEY,
    tax_invoice_no    TEXT    NOT NULL UNIQUE,  -- FPJ C(15)
    doc_date          TEXT    NOT NULL,
    sale_journal_no   TEXT,                     -- KLR C(15)
    sub_code          TEXT,
    is_posted         TEXT    DEFAULT 'N',
    stamp_duty        INTEGER DEFAULT 0,       -- MATERAI N(15,2) × 100
    changed_by        INTEGER,
    changed_at        TEXT
);

-- Source: SM CREATE.BAK d_mfa()
CREATE TABLE fixed_assets (
    id                INTEGER PRIMARY KEY,
    asset_code        TEXT    NOT NULL UNIQUE,  -- MFA C(15)
    name              TEXT    NOT NULL,         -- NAME C(40)
    account_code      TEXT,                     -- ACC C(15)
    location          TEXT,                     -- LOC C(6)
    purchase_date     TEXT,                     -- DATE
    depreciation_pct  INTEGER DEFAULT 0,       -- SUSUT N(6,2) × 100
    unit              TEXT,                     -- UNIT C(6)
    unit_price        INTEGER DEFAULT 0,       -- PRICE N(13,2) × 100
    qty_last          INTEGER DEFAULT 0,       -- QLAST N(13,2) × 100
    qty_in            INTEGER DEFAULT 0,
    qty_out           INTEGER DEFAULT 0,
    method            INTEGER DEFAULT 0,       -- METODE N(1): depreciation method
    residual_value    INTEGER DEFAULT 0,       -- RESIDU N(13,2) × 100
    memo              TEXT,                     -- MEMO C(10)
    period_code       TEXT    NOT NULL,
    changed_by        INTEGER,
    changed_at        TEXT
);

-- Source: SM CREATE.BAK d_tfa()
CREATE TABLE fixed_asset_accounts (
    id                INTEGER PRIMARY KEY,
    account_code      TEXT    NOT NULL UNIQUE,  -- ACC C(15)
    accum_account     TEXT,                     -- ACCAKM C(15)
    expense_account   TEXT,                     -- ACCBIAYA C(15)
    reclass_account   TEXT,                     -- ACCRL C(13)
    changed_by        INTEGER,
    changed_at        TEXT
);

-- Source: SM CREATE.BAK d_mfd()
-- Fixed asset balance detail — per-account values per asset
CREATE TABLE fixed_asset_details (
    id              INTEGER PRIMARY KEY,
    asset_code      TEXT    NOT NULL,          -- MFA C(15)
    val_last        INTEGER DEFAULT 0,         -- VLAST N(17,0) × 100
    val_in          INTEGER DEFAULT 0,         -- VIN N(17,0) × 100
    val_out         INTEGER DEFAULT 0,         -- VOUT N(17,0) × 100
    account_code    TEXT,                       -- ACC C(15)
    period_code     TEXT    NOT NULL
);

-- Source: SM CREATE.BAK d_bfa()+d_jfa()+d_kfa()+d_dfa()
-- Fixed asset transactions: purchase, sale, correction, depreciation
CREATE TABLE fixed_asset_transactions (
    id              INTEGER PRIMARY KEY,
    doc_type        TEXT    NOT NULL CHECK(doc_type IN (
        'ASSET_PURCHASE','ASSET_SALE','ASSET_CORRECTION','DEPRECIATION'
    )),
    journal_no      TEXT    NOT NULL UNIQUE,    -- NOJNL C(15)
    asset_code      TEXT    DEFAULT '',         -- MFA C(15) — DFA only
    doc_date        TEXT    NOT NULL CHECK(doc_date GLOB '????-??-??'),
    account_code    TEXT    DEFAULT '',         -- ACC C(15) — BFA/JFA only
    sub_code        TEXT    DEFAULT '',         -- SUB C(15) — BFA/JFA only
    ref             TEXT    DEFAULT '',         -- REF C(15) — BFA/JFA/KFA
    remark          TEXT,
    total_value     INTEGER DEFAULT 0,         -- VAL N(15,2) × 100
    remaining       INTEGER DEFAULT 0,         -- SISA N(13,2) × 100 — BFA/JFA only
    due_date        TEXT,                       -- DUEDATE — BFA/JFA only
    is_posted       TEXT    DEFAULT 'N',        -- POSTED/POSTING
    control         INTEGER DEFAULT 1 CHECK(control IN (0,1,2,3,4,5)),
    print_count     INTEGER DEFAULT 0,
    approved_by     INTEGER,
    period_code     TEXT    NOT NULL,
    register_id     TEXT    DEFAULT '01',
    legacy_source   TEXT    DEFAULT 'CS',
    changed_by      INTEGER,
    changed_at      TEXT
);

-- Source: SM CREATE.BAK d_bfd()+d_jfd()+d_kfd()+d_dfd()
CREATE TABLE fixed_asset_transaction_items (
    id              INTEGER PRIMARY KEY,
    journal_no      TEXT    NOT NULL REFERENCES fixed_asset_transactions(journal_no) ON DELETE RESTRICT,
    order_ref       TEXT    DEFAULT '',         -- OMS C(15) — BFD only
    account_code    TEXT    DEFAULT '',
    asset_code      TEXT    DEFAULT '',         -- MFA C(15)
    remark          TEXT,
    quantity        INTEGER DEFAULT 0,
    unit_price      INTEGER DEFAULT 0,
    value           INTEGER DEFAULT 0,
    direction       TEXT    DEFAULT '' CHECK(direction IN ('','D','K')),  -- DK — KFD/DFD
    accum_value     INTEGER DEFAULT 0,         -- VAKM — JFD only
    book_value      INTEGER DEFAULT 0,         -- VOLEH — JFD only
    gain_loss       INTEGER DEFAULT 0          -- VRL — JFD only
);

-- Source: SM CREATE.BAK d_kgr()
CREATE TABLE giro_conversions (
    id              INTEGER PRIMARY KEY,
    giro_account    TEXT    NOT NULL UNIQUE,    -- ACCGIRO C(15)
    bank_account    TEXT    NOT NULL,           -- ACCBANK C(15)
    changed_by      INTEGER,
    changed_at      TEXT
);

-- Source: SM CREATE.BAK d_dth()
CREATE TABLE invoices (
    id              INTEGER PRIMARY KEY,
    journal_no      TEXT    NOT NULL UNIQUE,
    doc_date        TEXT    NOT NULL CHECK(doc_date GLOB '????-??-??'),
    sub_code        TEXT    NOT NULL,
    account_code    TEXT    DEFAULT '',
    remark          TEXT,
    control         INTEGER DEFAULT 1 CHECK(control IN (0,1,2,3,4,5)),
    print_count     INTEGER DEFAULT 0,
    period_code     TEXT    NOT NULL,
    changed_by      INTEGER,
    changed_at      TEXT
);

-- Source: SM CREATE.BAK d_dtd()
CREATE TABLE invoice_lines (
    id              INTEGER PRIMARY KEY,
    journal_no      TEXT    NOT NULL REFERENCES invoices(journal_no) ON DELETE RESTRICT,
    sale_journal    TEXT    DEFAULT '',         -- KLR C(15)
    remark          TEXT,
    value           INTEGER DEFAULT 0,
    doc_date        TEXT,
    direction       TEXT    CHECK(direction IN ('D','K'))
);

-- Sync tracking for multi-register replication
-- Tracks per-register, per-table, per-direction progress
CREATE TABLE sync_log (
    id              INTEGER PRIMARY KEY,
    register_id     TEXT    NOT NULL,
    table_name      TEXT    NOT NULL,
    direction       TEXT    NOT NULL CHECK(direction IN ('push','pull')),
    last_applied_id INTEGER NOT NULL DEFAULT 0,  -- monotonic sync_queue.id cursor (clock-safe)
    last_synced_at  TEXT,
    record_count    INTEGER DEFAULT 0,
    status          TEXT    DEFAULT 'OK' CHECK(status IN ('OK','ERROR','PARTIAL')),
    last_error      TEXT,
    UNIQUE(register_id, table_name, direction)
);

-- ============================================================
-- Section 7: FTS5 Virtual Table & Sync Triggers
-- ============================================================

CREATE VIRTUAL TABLE products_fts USING fts5(
    product_code,
    barcode,
    name,
    content='products',
    content_rowid='id',
    tokenize='unicode61 remove_diacritics 2'
);

-- Keep FTS in sync with products table
CREATE TRIGGER products_fts_ai AFTER INSERT ON products BEGIN
    INSERT INTO products_fts(rowid, product_code, barcode, name)
    VALUES (new.id, new.product_code, new.barcode, new.name);
END;

CREATE TRIGGER products_fts_ad AFTER DELETE ON products BEGIN
    INSERT INTO products_fts(products_fts, rowid, product_code, barcode, name)
    VALUES ('delete', old.id, old.product_code, old.barcode, old.name);
END;

CREATE TRIGGER products_fts_au AFTER UPDATE ON products BEGIN
    INSERT INTO products_fts(products_fts, rowid, product_code, barcode, name)
    VALUES ('delete', old.id, old.product_code, old.barcode, old.name);
    INSERT INTO products_fts(rowid, product_code, barcode, name)
    VALUES (new.id, new.product_code, new.barcode, new.name);
END;

-- ============================================================
-- Section 7b: Sync Queue (Outbox Pattern)
-- ============================================================
-- Each register's local outbox for changes to be synced via JSON batches over SMB.
-- The C# sync worker drains pending rows, serializes to HMAC-signed JSON files.
-- INSERT/UPDATE triggers are lightweight (no payload) — row still exists at export time.
-- DELETE triggers capture json_object() payload since the row will be gone.
-- NEVER add sync triggers on: users, roles, config, audit_log, sync_queue.

CREATE TABLE sync_queue (
    id              INTEGER PRIMARY KEY,  -- monotonic, local per-register (hub tracks per source)
    register_id     TEXT    NOT NULL,
    table_name      TEXT    NOT NULL CHECK(table_name IN (
                        'products', 'product_barcodes',
                        'sales', 'purchases',
                        'cash_transactions',
                        'memorial_journals',
                        'orders',
                        'stock_transfers',
                        'stock_adjustments',
                        'members', 'subsidiaries',
                        'departments', 'discounts',
                        'accounts', 'locations'
                    )),
    record_key      TEXT    NOT NULL,     -- business key (product_code or journal_no)
    operation       TEXT    NOT NULL CHECK(operation IN ('I','U','D')),
    payload         TEXT    CHECK(payload IS NULL OR json_valid(payload)),
    created_at      TEXT    NOT NULL DEFAULT (datetime('now')),  -- UTC for cross-register safety
    synced_at       TEXT,
    status          TEXT    NOT NULL DEFAULT 'pending'
                           CHECK(status IN ('pending','synced','failed')),
    retry_count     INTEGER NOT NULL DEFAULT 0,
    last_error      TEXT
);

-- Covering index for the drain query: WHERE status='pending' ORDER BY id
CREATE INDEX idx_sync_queue_drain ON sync_queue(status, id)
    WHERE status = 'pending';

-- ============================================================
-- Section 7c: Sync Triggers
-- ============================================================
-- Triggers populate sync_queue on data changes.
-- Child tables (sale_items, purchase_items, etc.) are NOT triggered —
-- the C# export bundles children with their header at export time.
-- register_id is read from config with COALESCE to prevent NULL abort.

-- Protect register_id from accidental deletion
CREATE TRIGGER trg_config_protect_register_id
BEFORE DELETE ON config
WHEN OLD.key = 'register_id'
BEGIN
    SELECT RAISE(ABORT, 'Cannot delete register_id from config');
END;

-- ----- PRODUCTS (master data, hub → slaves) -----

CREATE TRIGGER trg_products_sync_i AFTER INSERT ON products
BEGIN
    INSERT INTO sync_queue(register_id, table_name, record_key, operation)
    VALUES (COALESCE((SELECT value FROM config WHERE key='register_id'), 'unknown'),
            'products', NEW.product_code, 'I');
END;

CREATE TRIGGER trg_products_sync_u AFTER UPDATE ON products
WHEN OLD.name != NEW.name
  OR OLD.price != NEW.price
  OR OLD.price1 != NEW.price1
  OR OLD.price2 != NEW.price2
  OR OLD.price3 != NEW.price3
  OR OLD.price4 != NEW.price4
  OR OLD.buying_price != NEW.buying_price
  OR OLD.cost_price != NEW.cost_price
  OR OLD.barcode != NEW.barcode
  OR OLD.dept_code != NEW.dept_code
  OR OLD.vendor_code != NEW.vendor_code
  OR OLD.status != NEW.status
  OR OLD.disc_pct != NEW.disc_pct
  OR OLD.qty_break2 != NEW.qty_break2
  OR OLD.qty_break3 != NEW.qty_break3
  OR OLD.open_price != NEW.open_price
  OR OLD.is_consignment != NEW.is_consignment
  OR OLD.vat_flag != NEW.vat_flag
BEGIN
    INSERT INTO sync_queue(register_id, table_name, record_key, operation)
    VALUES (COALESCE((SELECT value FROM config WHERE key='register_id'), 'unknown'),
            'products', NEW.product_code, 'U');
END;

CREATE TRIGGER trg_products_sync_d AFTER DELETE ON products
BEGIN
    INSERT INTO sync_queue(register_id, table_name, record_key, operation, payload)
    VALUES (COALESCE((SELECT value FROM config WHERE key='register_id'), 'unknown'),
            'products', OLD.product_code, 'D',
            json_object(
                'product_code', OLD.product_code,
                'name',         OLD.name,
                'dept_code',    OLD.dept_code,
                'status',       OLD.status
            ));
END;

-- ----- SALES (transactions, slave → hub) -----

CREATE TRIGGER trg_sales_sync_i AFTER INSERT ON sales
BEGIN
    INSERT INTO sync_queue(register_id, table_name, record_key, operation)
    VALUES (COALESCE((SELECT value FROM config WHERE key='register_id'), 'unknown'),
            'sales', NEW.journal_no, 'I');
END;

CREATE TRIGGER trg_sales_sync_u AFTER UPDATE ON sales
BEGIN
    INSERT INTO sync_queue(register_id, table_name, record_key, operation)
    VALUES (COALESCE((SELECT value FROM config WHERE key='register_id'), 'unknown'),
            'sales', NEW.journal_no, 'U');
END;

CREATE TRIGGER trg_sales_sync_d AFTER DELETE ON sales
BEGIN
    INSERT INTO sync_queue(register_id, table_name, record_key, operation, payload)
    VALUES (COALESCE((SELECT value FROM config WHERE key='register_id'), 'unknown'),
            'sales', OLD.journal_no, 'D',
            json_object(
                'journal_no',   OLD.journal_no,
                'doc_type',     OLD.doc_type,
                'period_code',  OLD.period_code,
                'register_id',  OLD.register_id,
                'control',      OLD.control,
                'cc_last4',     CASE WHEN length(OLD.cc_number) >= 4
                                     THEN substr(OLD.cc_number, -4)
                                     ELSE '' END
            ));
END;

-- ----- PURCHASES (transactions, slave → hub) -----

CREATE TRIGGER trg_purchases_sync_i AFTER INSERT ON purchases
BEGIN
    INSERT INTO sync_queue(register_id, table_name, record_key, operation)
    VALUES (COALESCE((SELECT value FROM config WHERE key='register_id'), 'unknown'),
            'purchases', NEW.journal_no, 'I');
END;

CREATE TRIGGER trg_purchases_sync_u AFTER UPDATE ON purchases
BEGIN
    INSERT INTO sync_queue(register_id, table_name, record_key, operation)
    VALUES (COALESCE((SELECT value FROM config WHERE key='register_id'), 'unknown'),
            'purchases', NEW.journal_no, 'U');
END;

CREATE TRIGGER trg_purchases_sync_d AFTER DELETE ON purchases
BEGIN
    INSERT INTO sync_queue(register_id, table_name, record_key, operation, payload)
    VALUES (COALESCE((SELECT value FROM config WHERE key='register_id'), 'unknown'),
            'purchases', OLD.journal_no, 'D',
            json_object(
                'journal_no',   OLD.journal_no,
                'doc_type',     OLD.doc_type,
                'period_code',  OLD.period_code,
                'register_id',  OLD.register_id,
                'control',      OLD.control
            ));
END;

-- ----- CASH TRANSACTIONS (transactions, slave → hub) -----

CREATE TRIGGER trg_cash_transactions_sync_i AFTER INSERT ON cash_transactions
BEGIN
    INSERT INTO sync_queue(register_id, table_name, record_key, operation)
    VALUES (COALESCE((SELECT value FROM config WHERE key='register_id'), 'unknown'),
            'cash_transactions', NEW.journal_no, 'I');
END;

CREATE TRIGGER trg_cash_transactions_sync_u AFTER UPDATE ON cash_transactions
BEGIN
    INSERT INTO sync_queue(register_id, table_name, record_key, operation)
    VALUES (COALESCE((SELECT value FROM config WHERE key='register_id'), 'unknown'),
            'cash_transactions', NEW.journal_no, 'U');
END;

CREATE TRIGGER trg_cash_transactions_sync_d AFTER DELETE ON cash_transactions
BEGIN
    INSERT INTO sync_queue(register_id, table_name, record_key, operation, payload)
    VALUES (COALESCE((SELECT value FROM config WHERE key='register_id'), 'unknown'),
            'cash_transactions', OLD.journal_no, 'D',
            json_object(
                'journal_no',   OLD.journal_no,
                'doc_type',     OLD.doc_type,
                'period_code',  OLD.period_code,
                'register_id',  OLD.register_id,
                'control',      OLD.control
            ));
END;

-- ----- MEMORIAL JOURNALS (transactions, slave → hub) -----

CREATE TRIGGER trg_memorial_journals_sync_i AFTER INSERT ON memorial_journals
BEGIN
    INSERT INTO sync_queue(register_id, table_name, record_key, operation)
    VALUES (COALESCE((SELECT value FROM config WHERE key='register_id'), 'unknown'),
            'memorial_journals', NEW.journal_no, 'I');
END;

CREATE TRIGGER trg_memorial_journals_sync_u AFTER UPDATE ON memorial_journals
BEGIN
    INSERT INTO sync_queue(register_id, table_name, record_key, operation)
    VALUES (COALESCE((SELECT value FROM config WHERE key='register_id'), 'unknown'),
            'memorial_journals', NEW.journal_no, 'U');
END;

CREATE TRIGGER trg_memorial_journals_sync_d AFTER DELETE ON memorial_journals
BEGIN
    INSERT INTO sync_queue(register_id, table_name, record_key, operation, payload)
    VALUES (COALESCE((SELECT value FROM config WHERE key='register_id'), 'unknown'),
            'memorial_journals', OLD.journal_no, 'D',
            json_object(
                'journal_no',   OLD.journal_no,
                'doc_type',     OLD.doc_type,
                'period_code',  OLD.period_code,
                'register_id',  OLD.register_id,
                'control',      OLD.control
            ));
END;

-- ----- ORDERS (transactions, slave → hub) -----

CREATE TRIGGER trg_orders_sync_i AFTER INSERT ON orders
BEGIN
    INSERT INTO sync_queue(register_id, table_name, record_key, operation)
    VALUES (COALESCE((SELECT value FROM config WHERE key='register_id'), 'unknown'),
            'orders', NEW.journal_no, 'I');
END;

CREATE TRIGGER trg_orders_sync_u AFTER UPDATE ON orders
BEGIN
    INSERT INTO sync_queue(register_id, table_name, record_key, operation)
    VALUES (COALESCE((SELECT value FROM config WHERE key='register_id'), 'unknown'),
            'orders', NEW.journal_no, 'U');
END;

CREATE TRIGGER trg_orders_sync_d AFTER DELETE ON orders
BEGIN
    INSERT INTO sync_queue(register_id, table_name, record_key, operation, payload)
    VALUES (COALESCE((SELECT value FROM config WHERE key='register_id'), 'unknown'),
            'orders', OLD.journal_no, 'D',
            json_object(
                'journal_no',   OLD.journal_no,
                'doc_type',     OLD.doc_type,
                'period_code',  OLD.period_code,
                'register_id',  OLD.register_id,
                'control',      OLD.control
            ));
END;

-- ----- STOCK TRANSFERS (transactions, slave → hub) -----

CREATE TRIGGER trg_stock_transfers_sync_i AFTER INSERT ON stock_transfers
BEGIN
    INSERT INTO sync_queue(register_id, table_name, record_key, operation)
    VALUES (COALESCE((SELECT value FROM config WHERE key='register_id'), 'unknown'),
            'stock_transfers', NEW.journal_no, 'I');
END;

CREATE TRIGGER trg_stock_transfers_sync_u AFTER UPDATE ON stock_transfers
BEGIN
    INSERT INTO sync_queue(register_id, table_name, record_key, operation)
    VALUES (COALESCE((SELECT value FROM config WHERE key='register_id'), 'unknown'),
            'stock_transfers', NEW.journal_no, 'U');
END;

CREATE TRIGGER trg_stock_transfers_sync_d AFTER DELETE ON stock_transfers
BEGIN
    INSERT INTO sync_queue(register_id, table_name, record_key, operation, payload)
    VALUES (COALESCE((SELECT value FROM config WHERE key='register_id'), 'unknown'),
            'stock_transfers', OLD.journal_no, 'D',
            json_object(
                'journal_no',   OLD.journal_no,
                'doc_type',     OLD.doc_type,
                'period_code',  OLD.period_code,
                'register_id',  OLD.register_id,
                'control',      OLD.control
            ));
END;

-- ----- STOCK ADJUSTMENTS (transactions, slave → hub) -----

CREATE TRIGGER trg_stock_adjustments_sync_i AFTER INSERT ON stock_adjustments
BEGIN
    INSERT INTO sync_queue(register_id, table_name, record_key, operation)
    VALUES (COALESCE((SELECT value FROM config WHERE key='register_id'), 'unknown'),
            'stock_adjustments', NEW.journal_no, 'I');
END;

CREATE TRIGGER trg_stock_adjustments_sync_u AFTER UPDATE ON stock_adjustments
BEGIN
    INSERT INTO sync_queue(register_id, table_name, record_key, operation)
    VALUES (COALESCE((SELECT value FROM config WHERE key='register_id'), 'unknown'),
            'stock_adjustments', NEW.journal_no, 'U');
END;

CREATE TRIGGER trg_stock_adjustments_sync_d AFTER DELETE ON stock_adjustments
BEGIN
    INSERT INTO sync_queue(register_id, table_name, record_key, operation, payload)
    VALUES (COALESCE((SELECT value FROM config WHERE key='register_id'), 'unknown'),
            'stock_adjustments', OLD.journal_no, 'D',
            json_object(
                'journal_no',   OLD.journal_no,
                'doc_type',     OLD.doc_type,
                'period_code',  OLD.period_code,
                'register_id',  OLD.register_id,
                'control',      OLD.control
            ));
END;

-- ============================================================
-- Section 7d: Config Seed Data
-- ============================================================

INSERT INTO config(key, value, description) VALUES
    ('schema_version', '1', 'Database schema version — incremented by each migration script'),
    ('register_id', NULL, 'REQUIRED: Set to 01, 02, or 03 before first use — app refuses to start if NULL'),
    ('store_name', 'TOKO YONICO', 'Store name for receipts'),
    ('current_period', NULL, 'Current accounting period YYYYMM — set on first use');

-- ============================================================
-- Section 8: Indexes
-- ============================================================

-- Product lookups (POS hot path — must be sub-50ms)
CREATE UNIQUE INDEX idx_products_code ON products(product_code);
CREATE INDEX idx_products_barcode ON products(barcode) WHERE barcode IS NOT NULL AND barcode != '';
CREATE INDEX idx_products_vendor ON products(vendor_code);
CREATE INDEX idx_products_category ON products(category_code);
CREATE INDEX idx_products_type ON products(product_type);

-- Product barcodes — first lookup for POS barcode scan
CREATE UNIQUE INDEX idx_product_barcodes_barcode ON product_barcodes(barcode);
CREATE INDEX idx_product_barcodes_product ON product_barcodes(product_code);

-- Subsidiaries
CREATE UNIQUE INDEX idx_subs_code ON subsidiaries(sub_code);
CREATE INDEX idx_subs_group ON subsidiaries(group_code, sub_code);
CREATE INDEX idx_subs_name ON subsidiaries(name);

-- Members
CREATE UNIQUE INDEX idx_members_code ON members(member_code);

-- Account balances
CREATE INDEX idx_acct_bal_period ON account_balances(period_code, account_code);
CREATE INDEX idx_acct_bal_account ON account_balances(account_code);

-- Discounts — lookup by product + date
CREATE INDEX idx_discounts_product ON discounts(product_code, date_end);

-- Sales
CREATE UNIQUE INDEX idx_sales_journal ON sales(journal_no);
CREATE INDEX idx_sales_period_date ON sales(period_code, doc_date, journal_no);
CREATE INDEX idx_sales_sub ON sales(sub_code, doc_date);
CREATE INDEX idx_sales_cashier ON sales(cashier, doc_date);
CREATE INDEX idx_sales_shift ON sales(shift, doc_date);
CREATE INDEX idx_sales_type ON sales(doc_type, period_code);
CREATE INDEX idx_sale_items_journal ON sale_items(journal_no);

-- Purchases
CREATE UNIQUE INDEX idx_purchases_journal ON purchases(journal_no);
CREATE INDEX idx_purchases_period_date ON purchases(period_code, doc_date, journal_no);
CREATE INDEX idx_purchases_sub ON purchases(sub_code, doc_date);
CREATE INDEX idx_purchase_items_journal ON purchase_items(journal_no);

-- Cash transactions
CREATE UNIQUE INDEX idx_cash_journal ON cash_transactions(journal_no);
CREATE INDEX idx_cash_period_date ON cash_transactions(period_code, doc_date, journal_no);
CREATE INDEX idx_cash_lines_journal ON cash_transaction_lines(journal_no);

-- Memorial journals
CREATE UNIQUE INDEX idx_memorial_journal ON memorial_journals(journal_no);
CREATE INDEX idx_memorial_period ON memorial_journals(period_code, doc_date);
CREATE INDEX idx_memorial_lines_journal ON memorial_journal_lines(journal_no);

-- Orders
CREATE UNIQUE INDEX idx_orders_journal ON orders(journal_no);
CREATE INDEX idx_orders_period ON orders(period_code, doc_date);
CREATE INDEX idx_order_items_journal ON order_items(journal_no);

-- Stock transfers
CREATE UNIQUE INDEX idx_transfers_journal ON stock_transfers(journal_no);
CREATE INDEX idx_transfers_period ON stock_transfers(period_code, doc_date);
CREATE INDEX idx_transfer_items_journal ON stock_transfer_items(journal_no);

-- Stock register — all 7 SM ACR composite indexes (period_code leads each)
CREATE UNIQUE INDEX idx_stock_reg_main ON stock_register(period_code, account_code, sub_code, product_code);
CREATE INDEX idx_stock_reg_inv ON stock_register(period_code, product_code, sub_code, account_code);
CREATE INDEX idx_stock_reg_acc_sub ON stock_register(period_code, account_code, sub_code);
CREATE INDEX idx_stock_reg_sub_acc ON stock_register(period_code, sub_code, account_code);
CREATE INDEX idx_stock_reg_acc_inv ON stock_register(period_code, account_code, product_code, sub_code);
CREATE INDEX idx_stock_reg_sub_acc_inv ON stock_register(period_code, sub_code, account_code, product_code);
CREATE INDEX idx_stock_reg_sub_inv ON stock_register(period_code, sub_code, product_code);

-- Stock movements (Kartu Persediaan, GL posting queue, period rollup)
CREATE INDEX idx_stock_mov_product ON stock_movements(product_code, doc_date);
CREATE INDEX idx_stock_mov_period ON stock_movements(period_code, product_code);
CREATE INDEX idx_stock_mov_unposted ON stock_movements(is_posted, period_code) WHERE is_posted = 0;
CREATE INDEX idx_stock_mov_journal ON stock_movements(journal_no);
CREATE INDEX idx_stock_mov_location ON stock_movements(location_code, period_code, product_code);

-- Stock adjustments (Pemakaian/Rusak/Hilang)
CREATE INDEX idx_stock_adj_period ON stock_adjustments(period_code, doc_type);
CREATE INDEX idx_stock_adj_location ON stock_adjustments(location_code);
CREATE INDEX idx_stock_adj_items_journal ON stock_adjustment_items(journal_no);
CREATE INDEX idx_stock_adj_items_product ON stock_adjustment_items(product_code);

-- GL details
CREATE INDEX idx_gl_journal ON gl_details(journal_no);
CREATE INDEX idx_gl_acc_date ON gl_details(period_code, account_code, doc_date, journal_no);
CREATE INDEX idx_gl_sub_inv ON gl_details(period_code, sub_code, product_code, account_code, journal_no);

-- Payables register
CREATE INDEX idx_payables_journal ON payables_register(journal_no);
CREATE INDEX idx_payables_due ON payables_register(period_code, due_date);
CREATE INDEX idx_payables_sub ON payables_register(period_code, sub_code, account_code, doc_date);
CREATE INDEX idx_payables_unpaid ON payables_register(due_date) WHERE is_paid = 'N';

-- Stock opname
CREATE INDEX idx_opname_product ON stock_opname(period_code, product_code);
CREATE INDEX idx_opname_date ON stock_opname(period_code, doc_date);

-- Price history
CREATE INDEX idx_price_hist_product ON price_history(period_code, product_code, doc_date);

-- Giro register
CREATE INDEX idx_giro_bg ON giro_register(giro_no);
CREATE INDEX idx_giro_acc ON giro_register(period_code, account_code, doc_date);
CREATE INDEX idx_giro_open ON giro_register(period_code, status) WHERE status = 'O';

-- Sale/purchase items by product (for sales analysis & goods receipt history)
CREATE INDEX idx_sale_items_product ON sale_items(product_code, journal_no);
CREATE INDEX idx_purchase_items_product ON purchase_items(product_code, journal_no);

-- Order register (d_ror indexes: sub+jnl+inv+acc, jnl, inv+acc)
CREATE INDEX idx_order_register_journal ON order_register(journal_no);
CREATE INDEX idx_order_register_sub ON order_register(period_code, sub_code, journal_no);

-- Points register by period (for period-close rollforward)
CREATE INDEX idx_points_reg_period ON points_register(period_code);

-- Pending sales by journal
CREATE INDEX idx_pending_sales_journal ON pending_sales(journal_no);

-- Discount partners by subsidiary (POS customer-specific discount lookup)
CREATE INDEX idx_discount_partners_sub ON discount_partners(sub_code, account_code);

-- Fixed asset transactions
CREATE UNIQUE INDEX idx_fa_txn_journal ON fixed_asset_transactions(journal_no);
CREATE INDEX idx_fa_txn_period ON fixed_asset_transactions(period_code, doc_date);
CREATE INDEX idx_fa_txn_items_journal ON fixed_asset_transaction_items(journal_no);
CREATE INDEX idx_fa_details_asset ON fixed_asset_details(asset_code, period_code);

-- ============================================================
-- Section 9: Audit Log
-- ============================================================

CREATE TABLE audit_log (
    id          INTEGER PRIMARY KEY,
    table_name  TEXT    NOT NULL,
    record_key  TEXT    NOT NULL,
    action      TEXT    NOT NULL CHECK(action IN ('INSERT','UPDATE','DELETE')),
    old_values  TEXT,                          -- JSON of changed fields
    new_values  TEXT,                          -- JSON of changed fields
    user_id     INTEGER,
    created_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE INDEX idx_audit_table ON audit_log(table_name, created_at);
CREATE INDEX idx_audit_key ON audit_log(record_key, table_name);

-- ============================================================
-- Section 10: Convenience Views
-- ============================================================

-- Current stock position per product
CREATE VIEW v_stock_position AS
SELECT
    sr.product_code,
    p.name AS product_name,
    sr.account_code,
    sr.sub_code,
    sr.period_code,
    sr.qty_last + sr.qty_in - sr.qty_out AS qty_current,
    sr.val_last + sr.val_in - sr.val_out AS val_current,
    sr.cost_price,
    sr.last_posted_at
FROM stock_register sr
LEFT JOIN products p ON p.product_code = sr.product_code
WHERE sr.period_code = (SELECT value FROM config WHERE key = 'current_period');

-- Trial balance for a period
CREATE VIEW v_trial_balance AS
SELECT
    a.account_code,
    a.account_name,
    a.account_group,
    a.level,
    a.is_detail,
    ab.period_code,
    ab.opening_balance,
    ab.debit_total,
    ab.credit_total,
    ab.opening_balance + ab.debit_total - ab.credit_total AS ending_balance
FROM accounts a
JOIN account_balances ab ON ab.account_code = a.account_code;

-- Product lookup with barcode search (POS hot path)
CREATE VIEW v_product_lookup AS
SELECT
    p.id,
    p.product_code,
    p.barcode,
    p.name,
    p.price,
    p.price1,
    p.price2,
    p.price3,
    p.unit,
    p.unit1,
    p.conversion1,
    p.qty_break2,
    p.qty_break3,
    p.open_price,
    p.is_consignment,
    p.vat_flag,
    p.cost_price,
    p.status
FROM products p
WHERE p.status = 'A';

-- AP aging buckets
CREATE VIEW v_ap_aging AS
SELECT
    pr.sub_code,
    s.name AS vendor_name,
    pr.journal_no,
    pr.doc_date,
    pr.due_date,
    pr.value,
    pr.payment_amount,
    pr.value - pr.payment_amount AS outstanding,
    CASE
        WHEN julianday('now') - julianday(pr.due_date) <= 0 THEN 'CURRENT'
        WHEN julianday('now') - julianday(pr.due_date) <= 30 THEN '1-30'
        WHEN julianday('now') - julianday(pr.due_date) <= 60 THEN '31-60'
        WHEN julianday('now') - julianday(pr.due_date) <= 90 THEN '61-90'
        ELSE '90+'
    END AS aging_bucket
FROM payables_register pr
LEFT JOIN subsidiaries s ON s.sub_code = pr.sub_code
WHERE pr.is_paid = 'N' AND pr.control != 3;
