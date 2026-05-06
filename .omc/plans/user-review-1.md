# Plan: user_review#1 (Issue #20) — Bundled PR

**Branch:** `feat/user-review-1` (off `main`)
**PR:** Single bundled PR referencing #20
**Date:** 2026-04-26

---

## Context

Issue #20 contains UX feedback from the store owner after initial Avalonia migration. 8 design decisions were locked via interview. Work is organized into 3 vertical slices, each producing one commit on the same branch. The Avalonia app (`Kasir.Avalonia`) is the target — WinForms `Kasir/Forms` are legacy and untouched.

**Key architectural facts discovered during investigation:**
- Avalonia views live in `Kasir.Avalonia/Forms/` (37 views across 8 directories + ShellWindow)
- ShellWindow is minimal (just a ContentControl) — no global footer/status bar exists yet
- SaleView.axaml has a "SUBTOTAL" TextBlock banner (line 8) and `TxtSearchInput` search box
- ProductView.axaml currently uses a 3-tab TabControl (Umum/Harga/Lain-lain) — must be replaced with single screen
- Product.cs model has `Barcode` property (line 9), `MarginPct` is missing from model but `margin_pct` exists in Schema.sql
- ProductBarcodeRepository is referenced in SalesService (lines 14, 34, 76-81) and tested in ProductRepositoryTests.cs (lines 15, 22, 76-82, 188-235)
- MigrationRunner uses `System.Data.SQLite` (not `Microsoft.Data.Sqlite`) — Migration_005 must match
- Validators.cs has `IsValidBarcode()` method (line 9) — must be removed or repurposed
- Avalonia forms do NOT inherit from WinForms BaseForm — they are UserControls with their own status handling (18 references to SetAction/ShowSuccess/ShowError/lblAction in SaleView alone)

---

## Work Objectives

1. Implement kembalian (change) banner + `+` cash shortcut in SaleView (Q2, Q8)
2. Add global NumericInputBehavior + Indonesian thousands formatter (Q6, Q7)
3. Add global FooterStatus helper with default hints across all 37 Avalonia views (Q5)
4. Drop barcode column + product_barcodes table via Migration_005 (Q3)
5. Re-layout ProductView as single screen with WholesaleTierDialog (Q4)

---

## Guardrails

### Must Have
- INTEGER money pattern (value x 100) everywhere
- C# 12 / .NET 10, Avalonia 12.x, AXAML + code-behind (no MVVM/ReactiveUI)
- Build after every class/module (`dotnet build Kasir.Avalonia`)
- TreatWarningsAsErrors=true
- All 247+ existing tests pass after each slice
- Idempotent migration (try/catch like Migration_002)

### Must NOT Have
- No Co-Authored-By: Claude in commit messages
- No floating-point money
- No barcode references remaining after Slice 3 (except `BarcodeScanner.cs` hardware class which is HID keyboard — rename parameter only)
- No TabControl in ProductView after Slice 3
- No breaking changes to SalesService public API signatures (parameter rename OK)

---

## Task Flow

```
[Slice 1: POS Payment & Input] --> GATE 1 --> [Slice 2: Footer Hints] --> GATE 2 --> [Slice 3: Schema + ProductView] --> GATE 3 --> PR
```

---

## Slice 1 — POS Payment & Input (Q2, Q6, Q7, Q8)

**Agent:** `executor` (model=opus)
**Estimated files:** 8 new + 4 modified

### Task 1.1: NumericInputBehavior (Q6)
**Create:** `Kasir.Avalonia/Behaviors/NumericInputBehavior.cs`
- Static helper class (not Avalonia Behavior<T> — keep it simple like BaseForm.ApplyFocusIndicator pattern)
- `public static void Attach(TextBox textBox)` method
- On GotFocus: if Text is "0", "0.00", or "0,00" -> clear; else SelectAll()
- Can be applied via code-behind: `NumericInputBehavior.Attach(TxtCash);`
- **Build checkpoint:** `dotnet build Kasir.Avalonia`

**Acceptance:** Calling `NumericInputBehavior.Attach(txt)` on any TextBox clears "0" on focus and SelectAll() on non-zero.

### Task 1.2: Indonesian Thousands Formatter (Q7)
**Create:** `Kasir.Avalonia/Behaviors/IndonesianCurrencyFormatter.cs`
- `public static void Attach(TextBox textBox)` method
- On TextChanged: parse digits only, format as `1.250.000` (dot thousands, no decimals)
- Preserve caret distance-from-right across reformats
- Uses `CultureInfo("id-ID")` for formatting (matches `Formatting.cs` pattern)
- INTEGER cents: the TextBox shows whole Rupiah (divide by 100 on display, multiply on read)
- `public static long ParseCents(string formatted)` helper to extract INTEGER cents value
- **Build checkpoint:** `dotnet build Kasir.Avalonia`

**Acceptance:** Typing "1250000" in an attached TextBox displays "1.250.000". Caret stays at correct position. ParseCents("1.250.000") returns 125000000L (cents).

### Task 1.3: Kembalian Banner State Machine (Q2)
**Modify:** `Kasir.Avalonia/Forms/POS/SaleView.axaml`
- Change the existing "SUBTOTAL" TextBlock (line 8) to use `x:Name="BannerLabel"`
- Add right-alignment to match existing FormatCurrency style

**Modify:** `Kasir.Avalonia/Forms/POS/SaleView.axaml.cs`
- Add banner state enum: `Normal`, `Tunai`, `Kembalian`
- `ShowKembalianBanner(long changeCents)` — sets BannerLabel.Text to `"KEMBALIAN: {FormatCurrency}"`, changes foreground to success color
- `ShowTunaiBanner(long cashCents)` — sets BannerLabel.Text to `"TUNAI: {FormatCurrency}"`
- `ResetBanner()` — reverts to `"SUBTOTAL"` + original color
- 10-second DispatcherTimer auto-revert as safety net
- On any keypress in TxtSearchInput while banner is showing: call ResetBanner() AND let the keypress pass through (do NOT swallow via e.Handled)
- **Build checkpoint:** `dotnet build Kasir.Avalonia`

**Acceptance:** After a sale completes, banner shows "KEMBALIAN: Rp 20.000". Any key in search box clears banner AND types into search. Auto-reverts after 10s if no input.

### Task 1.4: `+` Cash Shortcut in Search Box (Q8)
**Modify:** `Kasir.Avalonia/Forms/POS/SaleView.axaml.cs`
- In TxtSearchInput KeyDown handler:
  - If key is `+` AND current text is digits-only (after stripping dots from formatter):
    - Reinterpret digits as cash amount (whole Rupiah -> multiply by 100 for cents)
    - Show "TUNAI: {amount}" banner briefly
    - Complete the sale (reuse existing payment completion logic)
    - Then show "KEMBALIAN: {change}" banner (Q2 dismiss rules apply)
    - Clear search box
  - Existing `*` qty multiplier behavior preserved (check current implementation first)
- Ensure search box gets focus: on form load, after sale completes, after PaymentWindow closes
- **Build checkpoint:** `dotnet build Kasir.Avalonia`

**Acceptance:** Typing `70000+` with a 50,000 subtotal shows "TUNAI: Rp 70.000" then "KEMBALIAN: Rp 20.000". Search box stays focused throughout. Existing `*` multiplier still works.

### Task 1.5: Apply NumericInputBehavior + Formatter to PaymentWindow
**Modify:** `Kasir.Avalonia/Forms/POS/PaymentWindow.axaml.cs`
- Attach NumericInputBehavior + IndonesianCurrencyFormatter to TxtCash, TxtCard, TxtVoucher (or whatever the cash/card/voucher TextBox names are)
- **Build checkpoint:** `dotnet build Kasir.Avalonia`

**Acceptance:** PaymentWindow numeric fields clear "0" on focus, format with dots while typing.

### Verification Gate 1
```
dotnet build Kasir.Avalonia          # must succeed
dotnet test Kasir.Core.Tests         # 247+ tests pass
dotnet run --project Kasir.Avalonia  # manual smoke: POS flow, +shortcut, kembalian banner
```
**Agent:** `code-reviewer` on all changed files
**Commit message template:**
```
feat(pos): kembalian banner, + cash shortcut, numeric input helpers

- Kembalian banner replaces SUBTOTAL after sale with 10s auto-revert
- + key in search box triggers cash payment shortcut
- NumericInputBehavior clears zero on focus, SelectAll on non-zero
- IndonesianCurrencyFormatter live-formats 1.250.000 with caret preservation
- Applied to PaymentWindow cash/card/voucher fields

Closes partial #20
```

---

## Slice 2 — Footer Hints (Q5)

**Agent:** `executor` (model=opus)
**Estimated files:** 2 new + 37 modified (all Avalonia views)

### Task 2.1: FooterStatus Global Helper
**Create:** `Kasir.Avalonia/Infrastructure/FooterStatus.cs`
- Singleton or static class accessible from any view
- `public static void Show(string msg, int seconds = 3)` — displays message in ShellWindow footer area
- Uses DispatcherTimer. Cancels prior timer if new status arrives.
- `public static void SetDefault(string msg)` — registers the default hint for the current view
- After timer expires, reverts to current default hint
- Needs a TextBlock in ShellWindow to target

**Modify:** `Kasir.Avalonia/ShellWindow.axaml`
- Add a footer bar (DockPanel.Dock="Bottom") with a TextBlock `x:Name="LblFooterHint"` before the ContentArea
- Style: BgHeader background, FgPrimary foreground, small font
- **Build checkpoint:** `dotnet build Kasir.Avalonia`

**Modify:** `Kasir.Avalonia/ShellWindow.axaml.cs`
- Wire FooterStatus to the LblFooterHint TextBlock on window load

**Acceptance:** `FooterStatus.Show("Saved!", 3)` displays message for 3 seconds then reverts to default hint.

### Task 2.2: Register Default Hints Across All Views
**Modify:** All 37 Avalonia view `.axaml.cs` files (code-behind `OnLoaded` or constructor):

| Directory | Views | Default Hint |
|-----------|-------|-------------|
| POS/ | SaleView, PaymentWindow, ShiftView, CalculatorDialogWindow | "F5=Bayar F8=Void F10=Cari Esc=Keluar" (SaleView), contextual for others |
| Master/ | ProductView, VendorView, DepartmentView, CreditCardView, PriceChangeView | "F2=Simpan F3=Hapus F5=Cari Esc=Keluar" |
| Purchasing/ | PurchaseOrderView, GoodsReceiptView, PurchaseInvoiceView, ReturnView | "F2=Simpan F5=Cari Esc=Keluar" |
| Inventory/ | StockOutView, TransferView, OpnameView | "F2=Simpan F5=Cari Esc=Keluar" |
| Accounting/ | AccountsView, CashDisbursementView, CashReceiptView, JournalView, PayablesView, PostingProgressView | "F2=Simpan Esc=Keluar" |
| Reports/ | SalesReportView, InventoryReportView, FinancialReportView, ProductReportView, SupplierReportView | "F5=Cetak Esc=Keluar" |
| Bank/ | BankView, BankGiroView | "F2=Simpan Esc=Keluar" |
| Admin/ | UserView, BackupView, PrinterConfigView, UpdateView, AboutView, FirstRunView | "Esc=Keluar" |
| Shared/ | InputDialogWindow, MsgBoxWindow | (no hint — dialogs) |
| Root | LoginView, MainMenuView | "Enter=Login" / contextual |

- Replace any existing ad-hoc `lblAction.Text = "..."` patterns with `FooterStatus.Show(...)` calls
- **Build checkpoint after every 5-6 views:** `dotnet build Kasir.Avalonia`

**Acceptance:** Every view shows its default hint in the footer on load. Temporary messages (save success, error) show for 3 seconds then revert.

### Verification Gate 2
```
dotnet build Kasir.Avalonia          # must succeed
dotnet test Kasir.Core.Tests         # 247+ tests pass
dotnet run --project Kasir.Avalonia  # manual smoke: navigate 5+ views, verify footer hints
```
**Agent:** `code-reviewer` on all changed files
**Commit message template:**
```
feat(ui): global footer hint system with per-view defaults

- FooterStatus singleton with Show(msg, seconds) and SetDefault(msg)
- ShellWindow gets footer bar with DispatcherTimer revert
- All 37 Avalonia views register contextual keyboard hints
- Replaces ad-hoc status label patterns

Closes partial #20
```

---

## Slice 3 — Schema Cleanup + ProductView Redesign (Q3, Q4)

**Agent:** `executor` (model=opus)
**Estimated files:** 4 new + 12 modified + 3 deleted

### Task 3.1: Migration_005 — Drop Barcode
**Create:** `Kasir/Data/Migrations/Migration_005.cs`
- `Version = 5` (skip 3-4 in case they're needed for other work)
- Idempotent try/catch pattern matching Migration_002
- SQL:
  ```sql
  ALTER TABLE products DROP COLUMN barcode;
  DROP TABLE IF EXISTS product_barcodes;
  ```
  Note: SQLite before 3.35.0 doesn't support DROP COLUMN — check if bundled SQLite version supports it. If not, use table rebuild pattern (CREATE new, INSERT, DROP old, RENAME). Wrap each in try/catch.
- **Build checkpoint:** `dotnet build Kasir`

**Modify:** `Kasir/Data/MigrationRunner.cs`
- Add `new Migration_005()` to the Migrations list (line 15-16)
- Note: MigrationRunner uses `System.Data.SQLite` — migration must use same namespace
- **Build checkpoint:** `dotnet build Kasir`

**Acceptance:** Running MigrationRunner on a copy of `data/kasir.db` succeeds. `PRAGMA table_info(products)` shows no `barcode` column. `SELECT name FROM sqlite_master WHERE name='product_barcodes'` returns empty.

### Task 3.2: Schema.sql Cleanup
**Modify:** `Kasir/Data/Schema.sql`
- Remove `barcode TEXT` from products CREATE TABLE (line 199)
- Remove entire `product_barcodes` CREATE TABLE block (lines 239-248)
- Remove `barcode` from products_fts virtual table and its triggers (lines 1159, 1168-1181)
- Remove `idx_products_barcode` index (line 1517)
- Remove `idx_product_barcodes_barcode` and `idx_product_barcodes_product` indexes (lines 1523-1524)
- Remove `product_barcodes` from validation query (line 1197)
- Remove barcode from change-tracking trigger condition (line 1257)
- Remove barcode from POS hot path view/query (line 1698+)

**Acceptance:** Schema.sql has zero references to `barcode` or `product_barcodes`. Fresh DB creation from Schema.sql succeeds.

### Task 3.3: Delete Barcode Model + Repository
**Delete:** `Kasir/Models/ProductBarcode.cs`
**Delete:** `Kasir/Data/Repositories/ProductBarcodeRepository.cs`

**Modify:** `Kasir/Models/Product.cs`
- Remove `Barcode` property (line 9)

**Modify:** `Kasir/Data/Repositories/ProductRepository.cs`
- Remove `GetByBarcode()` method (line 24-29)
- Remove barcode fallback from search methods (lines 70-74, 146-150)
- Remove `barcode LIKE @q` from WHERE clauses (lines 81, 180)
- Remove `barcode` from INSERT column list + VALUES (lines 192-203)
- Remove `barcode` from UPDATE SET clause (lines 232-239)
- Remove `Barcode = SqlHelper.GetString(reader, "barcode")` from MapProduct (line 276)

**Modify:** `Kasir/Services/SalesService.cs`
- Remove `_barcodeRepo` field (line 14) and constructor init (line 34)
- Simplify `AddItem()`: remove barcode table lookup (lines 72-81), just do `_productRepo.GetByCode(codeOrBarcode)`
- Remove `barcodeOverridePrice` / `barcodeQty` variables and their usage (lines 72-73, 80-81, 99-100, 107, 141)
- Rename parameter `codeOrBarcode` to `productCode` in both overloads (lines 64, 69)

**Modify:** `Kasir/Services/PricingEngine.cs`
- Remove `barcodeOverride` parameter (line 16) and its priority block (lines 26-29)
- Update comment (line 9) — remove "barcode override" from priority list

**Modify:** `Kasir/Hardware/BarcodeScanner.cs`
- Keep the class (it's HID keyboard input handler, still needed)
- Review for any barcode-specific lookup logic — update if present

**Modify:** `Kasir/Utils/Validators.cs`
- Remove `IsValidBarcode()` method (line 9+)

**Modify:** `Kasir.Core.Tests/Data/ProductRepositoryTests.cs`
- Remove `_barcodeRepo` field (line 15) and init (line 22)
- Remove `GetByBarcode_ExistingBarcode_ReturnsProduct` test (line 76+)
- Remove `Barcode_InsertAndGetByBarcode` test (line 191+)
- Remove `Barcode_WithQtyPerScan_ReturnsCorrectQty` test (line 212+)
- Remove `Barcode_WithPriceOverride_ReturnsOverridePrice` test (line 231+)
- Remove `product.Barcode = ...` assignments in remaining tests (line 79)

**Modify:** `Kasir.Core.Tests/Utils/ValidatorTests.cs`
- Remove `IsValidBarcode_ValidatesCorrectly` test (line 20+)

- **Build checkpoint:** `dotnet build` (full solution)
- **Test checkpoint:** `dotnet test Kasir.Core.Tests`

**Acceptance:** Zero references to `ProductBarcode`, `ProductBarcodeRepository`, `GetByBarcode`, `barcodeOverride`, or `product.Barcode` in the codebase (verified with grep). All tests pass.

### Task 3.4: ProductView Single-Screen Redesign (Q4)
**Modify:** `Kasir.Avalonia/Forms/Master/ProductView.axaml` (full rewrite of layout)
- Remove TabControl (currently lines 61-158)
- New layout (top-down):
  - **Row 0:** DataGrid (product list, keep existing — has Stok Toko/Stok Gd columns)
  - **Row 1:** Single detail panel (replaces TabControl):
    - **Top row:** Kode | Nama | Departemen | Satuan | Supplier (horizontal)
    - **Second row:** Disc.Max% (`disc_pct`) | Profit% (`margin_pct`)
    - **Left column:** Harga Beli (`buying_price`) / Harga Pokok (`cost_price`) / Harga Jual (`price`) — vertical stack
    - **Right column:** Stok grid — 2 columns (Gudang, Toko) x 5 rows (Maximum, Ideal, Minimum, Awal, Sekarang)
  - F8 key handler opens WholesaleTierDialog
- Hidden fields (keep in DB, remove from form): unit2, conversion1/2, factor, is_consignment, open_price, account_code, category_code, location, type_sub, product_type, alt_vendor, shelf_location, lowest_cost, profit, vat_flag, luxury_tax_flag

**Modify:** `Kasir.Avalonia/Forms/Master/ProductView.axaml.cs`
- Bind disc_pct, margin_pct, cost_price, buying_price to form fields
- Populate Stok grid from stock table (needs stock query — check if StockRepository exists or use direct SQL)
- F8 KeyDown handler: open WholesaleTierDialog
- Remove TabControl switching logic

**Modify:** `Kasir/Models/Product.cs`
- Add `MarginPct` property (maps to `margin_pct` in schema — column exists, model property missing)

**Modify:** `Kasir/Data/Repositories/ProductRepository.cs`
- Add `margin_pct` to SELECT/INSERT/UPDATE/MapProduct

- **Build checkpoint:** `dotnet build Kasir.Avalonia`

**Acceptance:** ProductView shows all fields on one screen matching legacy DOS layout. No tabs. disc_pct and margin_pct display correctly. Stok grid populates from DB.

### Task 3.5: WholesaleTierDialog (Q4 — F8)
**Create:** `Kasir.Avalonia/Forms/Master/WholesaleTierDialog.axaml`
**Create:** `Kasir.Avalonia/Forms/Master/WholesaleTierDialog.axaml.cs`
- Modal dialog (Window, not UserControl)
- 4 rows: Tier 1 (Harga 1 = `price1`), Tier 2 (`price2` + `qty_break2`), Tier 3 (`price3` + `qty_break3`), Tier 4 (`price4`)
- Each row: Label | Price TextBox (with IndonesianCurrencyFormatter) | Qty>= TextBox (tiers 2-3 only)
- F2/Enter = Save, Esc = Cancel
- Returns modified Product with updated price1-4 + qty_break2/3
- **Build checkpoint:** `dotnet build Kasir.Avalonia`

**Acceptance:** F8 in ProductView opens dialog. Editing price2 + qty_break2 and pressing F2 saves to DB. Values round-trip correctly.

### Task 3.6: SQLite Round-Trip Verification
- Copy `data/kasir.db` to temp location
- Run Migration_005 on copy
- Verify: `PRAGMA table_info(products)` — no barcode column
- Verify: `SELECT count(*) FROM products` — still 24,457 rows
- Verify: `SELECT name FROM sqlite_master WHERE name='product_barcodes'` — empty
- Verify: Product CRUD operations work (insert, update, search by product_code)

**Acceptance:** All data intact, no barcode references, CRUD works.

### Verification Gate 3
```
dotnet build Kasir.Avalonia          # must succeed
dotnet test Kasir.Core.Tests         # 247+ tests pass (count may decrease by ~4 due to removed barcode tests)
dotnet run --project Kasir.Avalonia  # manual smoke: ProductView layout, F8 dialog, POS search
grep -rn "ProductBarcode\|GetByBarcode\|product_barcodes" Kasir/ Kasir.Core.Tests/  # must return 0
```
**Agents:**
- `database-reviewer` on Migration_005 + Schema.sql changes
- `code-reviewer` on all changed files

**Commit message template:**
```
feat(schema,product): drop barcode, redesign ProductView single-screen

- Migration_005: DROP barcode column + product_barcodes table (idempotent)
- Remove ProductBarcode model, repository, and all barcode lookup paths
- SalesService simplified to product_code-only lookup
- ProductView: single screen replacing 3-tab layout (matches legacy DOS)
- WholesaleTierDialog (F8): edit price1-4 + qty_break2/3
- Add MarginPct to Product model

Closes partial #20
```

---

## Final PR Gate

```
git push -u origin feat/user-review-1
gh run list --limit 1                # wait for CI green
```

### PR Body Template
```
## Summary
Implements user_review#1 feedback bundle from issue #20:

- **Kembalian banner**: Shows change amount after sale, auto-reverts after 10s, any keypress in search clears + types through
- **`+` cash shortcut**: Digits followed by `+` in search box triggers instant cash payment
- **Numeric input**: Zero-clear on focus, SelectAll on non-zero, live Indonesian thousands formatting (1.250.000)
- **Footer hints**: Global FooterStatus system with per-view keyboard hints across all 37 views
- **Drop barcode**: Migration_005 removes empty barcode column + product_barcodes table, simplifies SalesService to product_code-only
- **ProductView redesign**: Single-screen layout replacing 3-tab control, matches legacy DOS reference
- **Wholesale tiers**: F8 dialog for editing price1-4 + qty_break2/3

## Test plan
- [ ] `dotnet test Kasir.Core.Tests` — all tests pass
- [ ] POS flow: scan product, type `70000+`, verify kembalian banner shows then clears on keystroke
- [ ] POS flow: F5 multi-tender payment still works
- [ ] Navigate 5+ views — footer hints appear and revert after temporary messages
- [ ] ProductView: all fields visible on single screen, F8 opens wholesale dialog
- [ ] Fresh DB creation from Schema.sql succeeds (no barcode references)
- [ ] Migration_005 on existing kasir.db preserves all 24,457 products
- [ ] CI green on all 3 platforms (windows/macos/ubuntu)

Closes #20
```

---

## Success Criteria

1. Branch `feat/user-review-1` has exactly 3 clean commits (one per slice)
2. All 247+ tests pass (minus ~4 removed barcode tests, plus any new ones)
3. CI green on all platforms after push
4. Zero grep hits for `ProductBarcode`, `GetByBarcode`, `product_barcodes` outside of git history
5. Manual smoke test on Mac confirms: kembalian banner, + shortcut, footer hints, ProductView layout, F8 dialog
6. PR opened and referencing #20

---

## Risk Notes

- **SQLite DROP COLUMN**: Requires SQLite 3.35.0+. The bundled `SQLitePCLRaw.bundle_e_sqlite3 2.1.10` ships SQLite 3.45.1 which supports it. However, `System.Data.SQLite.Core` (used by MigrationRunner) may bundle a different version. **Executor must verify with `SELECT sqlite_version()` before committing to DROP COLUMN syntax.** Fallback: table rebuild pattern.
- **MigrationRunner namespace mismatch**: MigrationRunner.cs imports `System.Data.SQLite` but CLAUDE.md says to use `Microsoft.Data.Sqlite`. Migration_005 must match the existing MigrationRunner's namespace (`System.Data.SQLite`). Do not refactor MigrationRunner in this PR.
- **FTS5 triggers**: Removing `barcode` from the FTS5 virtual table and its 3 triggers (INSERT/DELETE/UPDATE) requires careful SQL. The FTS table must be dropped and recreated, or the triggers rebuilt. Executor should test with a fresh Schema.sql creation.
- **FooterStatus singleton lifecycle**: Must survive view navigation. Wire to ShellWindow which persists across ContentArea swaps.
