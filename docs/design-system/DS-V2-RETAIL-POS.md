# Design System v2 — Retail POS Migration Plan

**Branch**: `feat/design-system-v2`
**Worktree**: `~/Code/kasir-worktrees/ds-v2`
**Source bundle**: `/tmp/design-pkg/kasir-pos-design-system/project/`
**Repo**: `kasir-pos/` (remote: `origin https://github.com/Panandika/kasir-pos.git`)
**Base**: `main`

## Requirements Summary

1. Replace terminal-CRT green theme with v2 retail-POS design system (neutral surfaces, teal-green accent)
2. Dark + Light themes via `DynamicResource` everywhere, toggle `Ctrl+Shift+L`, persisted
3. Inter (via `Avalonia.Fonts.Inter` 12.0.1 NuGet, already in csproj) + JetBrains Mono (bundled TTF)
4. Density tokens: Compact 28 / Default 32 / Comfortable 40 per FOUNDATIONS.md
5. Lucide.Avalonia (v0.2.5, MIT, dme-compunet) for Lucide iconography
6. Sync chrome status bar badge per FOUNDATIONS.md state machine
7. All-in single PR, work in git worktree
8. 40 AXAML forms (not 34 -- includes FirstRunView, UpdateView, WholesaleTierDialog, MsgBoxWindow, ProductReportView, SupplierReportView)

---

## RALPLAN-DR

### Principles

1. **Token-first theming** -- every visual property resolves through named resource keys; no hardcoded colors survive
2. **Build-after-every-module** -- compile after each file change; never batch (per CLAUDE.md guardrail)
3. **Density follows function** -- Sale=Default(32), Reports/Master=Compact(28), touch-only=Comfortable(40)
4. **Minimal code-behind color** -- move all color decisions to AXAML bindings or dynamic resource lookups; code-behind only for conditional logic that truly requires runtime switching
5. **Reversible delivery** -- single PR, no schema changes, revert = revert PR

### Decision Drivers

1. **Theme-switch correctness**: every surface must respond to live variant change without restart
2. **Regression safety**: 247+ existing tests must stay green; new theme-token test project catches resource resolution failures
3. **Form count accuracy**: all 40 forms must be swept, not 34

### Viable Options

**Option A: Single all-in PR (CHOSEN)**
- Pros: atomic review, single revert point, no intermediate broken states on main
- Cons: large PR (~60 files), harder to review in one pass
- Mitigation: phased commits within PR; each phase is a separate commit with build verification

**Option B: Phased PRs per area (tokens, fonts, forms, chrome)**
- Pros: smaller reviews, incremental merge
- Cons: intermediate states on main where theme is half-old half-new; forms reference tokens that may not exist yet if merged out of order; user explicitly rejected this approach
- **Invalidated**: user confirmed all-in single PR

**Option C: Shim-only swap (recolor ThemeConstants, no DynamicResource migration)**
- Pros: fastest, minimal diff
- Cons: no theme toggle, no light mode, static brushes remain frozen at startup, does not meet requirements
- **Invalidated**: requirement #2 mandates live theme switching via DynamicResource

### ADR

- **Decision**: Option A -- single all-in PR with phased internal commits
- **Drivers**: user requirement for all-in; theme-switch correctness demands atomic token+form migration; single revert point
- **Alternatives considered**: B (phased PRs), C (shim-only)
- **Why chosen**: only option satisfying all requirements; phased commits within PR give reviewability without merge-order risk
- **Consequences**: large PR requires disciplined per-phase commits and thorough verification; reviewer should check phase-by-phase via commit log
- **Follow-ups**: after merge, schedule audit of any new forms added post-migration to ensure they use design system tokens

---

## P0 -- Worktree Setup & Punch List

### Worktree Commands (verbatim)

```bash
# Create worktree directory
mkdir -p ~/Code/kasir-worktrees

# Create worktree from kasir-pos repo
cd ~/Code/kasir/kasir-pos
git worktree add ~/Code/kasir-worktrees/ds-v2 -b feat/design-system-v2

# Verify
cd ~/Code/kasir-worktrees/ds-v2
git branch --show-current   # expect: feat/design-system-v2
dotnet build                # expect: clean build
dotnet test Kasir.Core.Tests # expect: 247+ passing
```

### Punch List Grep Commands

Run these from `~/Code/kasir-worktrees/ds-v2/Kasir.Avalonia/` to establish baseline:

```bash
# 1. ThemeConstants brush usage in code-behind (expect: 7 callsites in 2 files; declarations in ThemeConstants.cs excluded)
grep -rn "ThemeConstants\.\(.*Brush\)" --include="*.cs" --exclude=ThemeConstants.cs
# Expected output:
#   Forms/Shared/InputDialogWindow.axaml.cs:28  ThemeConstants.DisabledBrush
#   Forms/Shared/InputDialogWindow.axaml.cs:36  ThemeConstants.InputBackBrush
#   Forms/Shared/InputDialogWindow.axaml.cs:37  ThemeConstants.ForegroundBrush
#   Converters/StockColorConverter.cs:15         ThemeConstants.ErrorBrush
#   Converters/StockColorConverter.cs:17         ThemeConstants.DisabledBrush
#   Converters/StockColorConverter.cs:18         ThemeConstants.ForegroundBrush
#   Converters/StockColorConverter.cs:20         ThemeConstants.ForegroundBrush

# 2. ThemeConstants font usage (expect: 3 callsites in InputDialogWindow)
grep -rn "ThemeConstants\.\(FontFamily\|FontSize\)" --include="*.cs"

# 3. Hardcoded colors in code-behind (expect: 5 callsites in 2 files)
grep -rn "Colors\.\|Brush\.Parse\|new SolidColorBrush" --include="*.cs"
# Expected:
#   Forms/Admin/FirstRunView.axaml.cs:51   Brush.Parse("#008800")
#   Forms/Admin/FirstRunView.axaml.cs:56   Brush.Parse("#ff5050")
#   Forms/POS/SaleView.axaml.cs:331        Colors.LimeGreen
#   Forms/POS/SaleView.axaml.cs:332        Colors.OrangeRed
#   Forms/POS/SaleView.axaml.cs:372        Colors.LimeGreen

# 4. StaticResource for Brush/Color keys that should be DynamicResource (expect: 0; converter/style refs like StaticResource StockColorConverter are OK)
grep -rn "StaticResource.*Brush\|StaticResource.*Color" --include="*.axaml" Forms/

# 5. Hardcoded Background="#" in AXAML (expect: ShellWindow.axaml:6 only)
grep -rn 'Background="#' --include="*.axaml"

# 6. Form file count (expect: 40)
find Forms/ -name "*.axaml" | wc -l

# 7. SaleView fixed footer heights (expect: RowDefinitions="2.5*,7.5*,40,24")
grep -n "RowDefinitions" Forms/POS/SaleView.axaml
```

---

## P1 -- Token Infrastructure (BaseTheme.axaml rewrite)

### Architecture: Avalonia 12 Two-Variant Pattern

Avalonia 12 uses nested `<Styles>` blocks with `ThemeVariant` attribute -- NOT separate ResourceInclude files:

```xml
<Styles xmlns="https://github.com/avaloniaui" ...>
  <!-- Shared (variant-independent) resources -->
  <Styles.Resources>
    <ResourceDictionary>
      <!-- fonts, density, radius tokens here -->
    </ResourceDictionary>
  </Styles.Resources>

  <!-- Dark variant tokens -->
  <Styles ThemeVariant="Dark">
    <Styles.Resources>
      <ResourceDictionary>
        <!-- 20 dark color tokens -->
      </ResourceDictionary>
    </Styles.Resources>
  </Styles>

  <!-- Light variant tokens -->
  <Styles ThemeVariant="Light">
    <Styles.Resources>
      <ResourceDictionary>
        <!-- 20 light color tokens -->
      </ResourceDictionary>
    </Styles.Resources>
  </Styles>

  <!-- Control styles (variant-independent, use DynamicResource) -->
  <Style Selector="TextBlock"> ... </Style>
  ...
</Styles>
```

### StaticResource vs DynamicResource Rule

Within `<Styles ThemeVariant="X">` blocks, Color-to-Brush bindings (e.g., `<SolidColorBrush x:Key="Bg0Brush" Color="{StaticResource Bg0Color}"/>`) MUST use `StaticResource` because the Color is defined in the same scope. Only consumer references in forms (e.g., `Background="{DynamicResource Bg0Brush}"`) use `DynamicResource` for live variant switching. Mixing this up causes silent failures: `DynamicResource` for same-scope Color lookup may resolve to null during variant switch.

### Full Token Tables

**Dark Variant (20 tokens)** -- from `colors_and_type.css [data-theme="dark"]`:

| Token Key | Color Hex | Brush Key |
|-----------|-----------|-----------|
| Bg0Color | #FF0F1419 | Bg0Brush |
| Bg1Color | #FF161B22 | Bg1Brush |
| Bg2Color | #FF1C232C | Bg2Brush |
| BgHoverColor | #FF222A34 | BgHoverBrush |
| BgSelectedColor | #FF133127 | BgSelectedBrush |
| AccentBgColor | #FF2A1414 | AccentBgBrush |
| BorderSubtleColor | #FF232B36 | BorderSubtleBrush |
| BorderStrongColor | #FF2E3744 | BorderStrongBrush |
| FgPrimaryColor | #FFE6EDF3 | FgPrimaryBrush |
| FgSecondaryColor | #FF9BA7B4 | FgSecondaryBrush |
| FgDimColor | #FF6B7480 | FgDimBrush |
| FgNumericColor | #FFE6EDF3 | FgNumericBrush |
| FgOnBrandColor | #FFFFFFFF | FgOnBrandBrush |
| BrandColor | #FF2DBA8E | BrandBrush |
| BrandStrongColor | #FF56D3A8 | BrandStrongBrush |
| BrandSoftColor | #FF133127 | BrandSoftBrush |
| SuccessColor | #FF4ADE80 | SuccessBrush |
| WarningColor | #FFF59E0B | WarningBrush |
| DangerColor | #FFF87171 | DangerBrush |
| FocusRingColor | #FF2DBA8E | FocusRingBrush |

**Light Variant (20 tokens)** -- from `colors_and_type.css :root`:

| Token Key | Color Hex | Brush Key |
|-----------|-----------|-----------|
| Bg0Color | #FFF7F8FA | Bg0Brush |
| Bg1Color | #FFFFFFFF | Bg1Brush |
| Bg2Color | #FFF1F3F6 | Bg2Brush |
| BgHoverColor | #FFEEF1F4 | BgHoverBrush |
| BgSelectedColor | #FFE6F4EF | BgSelectedBrush |
| AccentBgColor | #FFFDECEC | AccentBgBrush |
| BorderSubtleColor | #FFE4E7EC | BorderSubtleBrush |
| BorderStrongColor | #FFCFD4DA | BorderStrongBrush |
| FgPrimaryColor | #FF111827 | FgPrimaryBrush |
| FgSecondaryColor | #FF4B5563 | FgSecondaryBrush |
| FgDimColor | #FF8A93A0 | FgDimBrush |
| FgNumericColor | #FF111827 | FgNumericBrush |
| FgOnBrandColor | #FFFFFFFF | FgOnBrandBrush |
| BrandColor | #FF0F8B6C | BrandBrush |
| BrandStrongColor | #FF0B6E55 | BrandStrongBrush |
| BrandSoftColor | #FFE6F4EF | BrandSoftBrush |
| SuccessColor | #FF15803D | SuccessBrush |
| WarningColor | #FFB45309 | WarningBrush |
| DangerColor | #FFB91C1C | DangerBrush |
| FocusRingColor | #FF0F8B6C | FocusRingBrush |

### Shared (Variant-Independent) Resources

```
Density:
  RowHeight = 32  (was 22)
  RowHeightCompact = 28
  RowHeightComfortable = 40
  ControlHeight = 36  (was 24)
  ControlHeightLg = 44

Typography:
  InterFont = "avares://Avalonia.Fonts.Inter#Inter"  (from NuGet)
  JetBrainsMonoFont = "avares://Kasir.Avalonia/Assets/Fonts#JetBrains Mono"
  FontSizeXs = 11
  FontSizeSm = 12
  FontSizeBase = 14  (was 12)
  FontSizeMd = 15
  FontSizeLg = 18
  FontSizeXl = 22
  FontSize2xl = 28
  FontSize3xl = 40   (also aliased as Fs3xl for AXAML DynamicResource binding)
  FontSize4xl = 56   (also aliased as Fs4xl for AXAML DynamicResource binding)

Radius:
  RadiusSm = 4
  Radius = 6
  RadiusMd = 8
  RadiusLg = 12

Spacing:
  Space1 = 4, Space2 = 8, Space3 = 12
  Space4 = 16, Space5 = 24, Space6 = 32, Space7 = 48
```

### Style Classes to Define

```
DataGrid.compact       → RowHeight=28, FontSize=FontSizeSm
DataGrid.comfortable   → RowHeight=40
Button.primary         → Background=BrandBrush, Foreground=FgOnBrandBrush
Button.danger          → Background=AccentBgBrush, Foreground=DangerBrush
Button.ghost           → Background=Transparent, BorderBrush=Transparent
Button.lg              → Height=ControlHeightLg, FontSize=FontSizeMd
TextBlock.numeric      → FontFamily=JetBrainsMonoFont, tabular-nums
TextBlock.label        → FontSize=FontSizeXs, Foreground=FgSecondaryBrush, uppercase
Border.card            → Background=Bg1Brush, Border=BorderSubtleBrush, CornerRadius=RadiusMd
Border.panel           → Background=Bg1Brush, Border=BorderSubtleBrush, CornerRadius=Radius
Badge.ok/warn/err/brand → pill badges per FOUNDATIONS.md
```

### ThemeConstants.cs Changes

- Delete ALL `Color` fields and ALL `IBrush` fields (lines 10-47)
- Delete `FontFamily` const (line 49)
- Keep only: `FontSize = 14` (update from 13), `HeaderFontSize = 15`, `StatusFontSize = 12`
- Or delete entire file if no code-behind references remain after P5a cleanup

### App.axaml Changes

- Keep `RequestedThemeVariant="Dark"` (default)
- ThemeService will override at runtime from persisted preference

### Acceptance Criteria

- [x] `dotnet build` clean
- [x] BaseTheme.axaml has Dark + Light variant blocks with all 40 brush keys (20 tokens x 2)
- [x] Old CRT green colors are gone from BaseTheme
- [x] Font family resources point to Inter (NuGet) and JetBrains Mono (bundled)
- [x] Existing forms render (may look different but no crashes)

---

## P2 -- Font Bundling (JetBrains Mono)

Inter is already provided by `Avalonia.Fonts.Inter` 12.0.1 NuGet -- no bundling needed.

### Steps

1. Download JetBrains Mono TTF files to `Kasir.Avalonia/Assets/Fonts/`:
   - `JetBrainsMono-Regular.ttf`
   - `JetBrainsMono-Bold.ttf`
   - Source: `/tmp/design-pkg/kasir-pos-design-system/project/fonts/` (woff2 in bundle; download TTF from GitHub releases or convert)
   - If only woff2 available: download TTF from https://github.com/JetBrains/JetBrainsMono/releases

2. Add to `Kasir.Avalonia.csproj`:
   ```xml
   <ItemGroup>
     <AvaloniaResource Include="Assets/Fonts/**" />
   </ItemGroup>
   ```

3. BaseTheme.axaml font resource (already defined in P1):
   ```xml
   <FontFamily x:Key="JetBrainsMonoFont">avares://Kasir.Avalonia/Assets/Fonts#JetBrains Mono</FontFamily>
   ```

4. Update `App.axaml` to include Inter font provider (already in place via NuGet):
   ```xml
   <!-- Avalonia.Fonts.Inter NuGet auto-registers Inter font family -->
   ```

### Acceptance Criteria

- [x] `dotnet build` clean
- [x] JetBrains Mono renders in DataGrid numeric columns
- [x] Inter renders in TextBlock and Button text
- [x] Font files are < 500KB total (TTF subset)

---

## P3 -- ThemeService + Toggle

### New File: `Kasir.Avalonia/Infrastructure/ThemeService.cs`

Responsibilities:
- Load theme preference on startup (from `theme.json` in app local data dir)
- `Toggle()` flips `Application.Current.RequestedThemeVariant` between `ThemeVariant.Dark` and `ThemeVariant.Light`
- Persist preference on toggle
- Expose `CurrentTheme` property and `ThemeChanged` event

### Persistence Strategy

`appsettings.json` does NOT exist under `Kasir.Avalonia/`. Instead of creating one (complicates publish), use a simple `theme.json` in `Environment.SpecialFolder.LocalApplicationData`:

```
Path: {LocalAppData}/KasirPOS/theme.json
Content: { "theme": "dark" }
```

Fallback: if file missing or unreadable, default to Dark.

### KeyboardRouter Extension

`KeyboardRouter.IsCtrlL()` (line 44) maps `Ctrl+L` = Lock register (per KEYBOARD.md). Theme toggle is `Ctrl+Shift+L`. Add new method:

```csharp
public static bool IsCtrlShiftL(KeyEventArgs e) =>
    e.Key == Key.L && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift);
```

### ShellWindow Changes

`ShellWindow.axaml`:
- Remove hardcoded `Background="#000000"` -- use `Background="{DynamicResource Bg0Brush}"`
- Add status bar panel (36px, `Bg1Brush`, bottom-docked) with sync badge placeholder
- Add hint bar strip below title bar

`ShellWindow.axaml.cs`:
- Subscribe to `KeyDown` for `Ctrl+Shift+L` -> `ThemeService.Toggle()`
- Initialize ThemeService in constructor

### Acceptance Criteria

- [x] `Ctrl+Shift+L` toggles theme live, entire UI flips
- [x] Theme persists across app restart
- [x] `Ctrl+L` still works for register lock (no conflict)
- [x] ShellWindow background follows theme

---

## P4 -- Icons (Lucide.Avalonia)

### Steps

1. Add NuGet: `dotnet add package Lucide.Avalonia` (v0.2.5, MIT, auto-updated weekly from upstream Lucide)

2. No `StyleInclude` or `xmlns` required (per package docs). Icons are used directly:
   ```xml
   <lucide:LucideIcon Kind="ShoppingCart" Size="24" />
   ```
   Add namespace in AXAML: `xmlns:lucide="clr-namespace:Lucide.Avalonia;assembly=Lucide.Avalonia"`

3. Icon mapping table (Lucide icon names):

   | Lucide Name | Usage |
   |-------------|-------|
   | ShoppingCart | Penjualan menu tile |
   | Truck | Pembelian menu tile |
   | Package | Inventori menu tile |
   | BookOpen | Akuntansi menu tile |
   | Database | Master menu tile |
   | Landmark | Bank menu tile |
   | BarChart3 | Laporan menu tile |
   | Settings | Admin menu tile |
   | ScanLine | Barcode input |
   | CreditCard | Payment |
   | Banknote | Cash payment |
   | User | User/login |
   | Search | Search |
   | Plus | Add |
   | Minus | Remove |
   | X | Close/cancel |
   | Check | Confirm |
   | Sun | Light theme |
   | Moon | Dark theme |
   | AlertCircle | Error/warning |

4. Replace text glyphs in `MainMenuView.axaml` tiles with `<lucide:LucideIcon Kind="..." />`
5. Add Sun/Moon toggle icon to ShellWindow title area

### Acceptance Criteria

- [x] `dotnet build` clean
- [x] MainMenu tiles show Lucide icons
- [x] Sun/Moon icon in title bar reflects current theme
- [x] Icons inherit `Foreground` color from parent (theme-responsive)

---

## P5a -- Code-Behind Hardcoded Color Cleanup

### Exact Fix List

**File 1: `Converters/StockColorConverter.cs` (lines 14-20)**
- Problem: returns static `ThemeConstants.*Brush` -- frozen at startup, won't theme-switch
- Fix: cache brush references lazily, invalidate on theme change. Do NOT call `FindResource()` on every `Convert()` — this is a hot path (24K product rows).

  ```csharp
  public class StockColorConverter : IValueConverter
  {
      private IBrush? _dangerBrush;
      private IBrush? _dimBrush;
      private IBrush? _primaryBrush;
      private bool _subscribed;

      private void EnsureInitialized()
      {
          if (!_subscribed)
          {
              _subscribed = true;
              Application.Current!.ActualThemeVariantChanged += (_, _) => InvalidateCache();
          }
          _dangerBrush ??= Application.Current!.FindResource("DangerBrush") as IBrush ?? Brushes.Red;
          _dimBrush ??= Application.Current!.FindResource("FgDimBrush") as IBrush ?? Brushes.Gray;
          _primaryBrush ??= Application.Current!.FindResource("FgPrimaryBrush") as IBrush ?? Brushes.White;
      }

      private void InvalidateCache()
      {
          _dangerBrush = null;
          _dimBrush = null;
          _primaryBrush = null;
          // Trigger DataGrid refresh if needed via messenger/event
      }

      public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
      {
          EnsureInitialized();
          if (value is int stock)
          {
              if (stock < 0) return _dangerBrush;
              if (stock == 0) return _dimBrush;
              return _primaryBrush;
          }
          return _primaryBrush;
      }
      // ... ConvertBack throws
  }
  ```
  
  Reference: `Kasir.Avalonia/Converters/StockColorConverter.cs:10-21`

**File 2: `Forms/Shared/InputDialogWindow.axaml.cs` (lines 28-39)**
- Problem: 7 callsites using `ThemeConstants.*Brush` and `ThemeConstants.FontFamily/FontSize`
- Fix: replace each with `Application.Current!.FindResource("...")`:
  - Line 28: `ThemeConstants.DisabledBrush` -> `FindResource("FgDimBrush")`
  - Line 29: `ThemeConstants.FontFamily` -> `FindResource("InterFont")` (cast to FontFamily)
  - Line 30: `ThemeConstants.FontSize` -> `FindResource("FontSizeBase")` (cast to double)
  - Line 36: `ThemeConstants.InputBackBrush` -> `FindResource("Bg1Brush")`
  - Line 37: `ThemeConstants.ForegroundBrush` -> `FindResource("FgPrimaryBrush")`
  - Line 38: `ThemeConstants.FontFamily` -> `FindResource("InterFont")`
  - Line 39: `ThemeConstants.FontSize` -> `FindResource("FontSizeBase")`
- Alternative: move entire InputDialog layout to AXAML with `{DynamicResource}` bindings (preferred if feasible)

**File 3: `Forms/Admin/FirstRunView.axaml.cs` (lines 51, 56)**
- Line 51: `Brush.Parse("#008800")` -> `FindResource("SuccessBrush")`
- Line 56: `Brush.Parse("#ff5050")` -> `FindResource("DangerBrush")`

**File 4: `Forms/POS/SaleView.axaml.cs` (lines 331-332, 372)**
- Lines 331-332: `Colors.LimeGreen` / `Colors.OrangeRed` -> `FindResource("SuccessBrush")` / `FindResource("DangerBrush")`
- Line 372: `Colors.LimeGreen` -> `FindResource("BrandBrush")` (subtotal uses brand color per design system)

### Acceptance Criteria

- [x] `grep -rn "ThemeConstants\.\|Colors\.\|Brush\.Parse" --include="*.cs"` returns 0 results in Forms/ and Converters/
- [x] `ThemeConstants.cs` can be deleted (or reduced to font size constants only)
- [x] `dotnet build` clean
- [x] StockColorConverter returns correct colors in both themes

---

## P5b -- SaleView Footer Refactor

### Problem

`SaleView.axaml:5` has `RowDefinitions="2.5*,7.5*,40,24"` -- the fixed `40` (hint bar) and `24` (status) break under density changes.

### Fix

Replace with token-bound heights:
```xml
<Grid RowDefinitions="2.5*,7.5*,Auto,Auto">
```

- Row 2 (hint bar): use `Height="{DynamicResource ControlHeight}"` on the content panel (resolves to 36)
- Row 3 (status): use `Height="Auto"` -- content determines height, or bind to a `StatusBarHeight` token (36 per design system)

### Acceptance Criteria

- [x] Sale screen footer adapts to density changes
- [x] 10 items still fit without scrolling at Default density
- [x] SaleView SUBTOTAL row 0 retains `FontSize=40` (label) and `FontSize=56` (value) per design spec -- these are the `--fs-3xl` and `--fs-4xl` tokens. Convert to `{DynamicResource Fs3xl}` and `{DynamicResource Fs4xl}` referencing token values 40 and 56. Row 0 proportional sizing (`2.5*`) unchanged
- [x] `dotnet build` clean

---

## P5c -- Density Classes

### Compact (28px rows) -- apply `Classes="compact"` to DataGrids in:

| Form | File |
|------|------|
| SalesReportView | Forms/Reports/SalesReportView.axaml |
| InventoryReportView | Forms/Reports/InventoryReportView.axaml |
| FinancialReportView | Forms/Reports/FinancialReportView.axaml |
| ProductReportView | Forms/Reports/ProductReportView.axaml |
| SupplierReportView | Forms/Reports/SupplierReportView.axaml |
| ProductView | Forms/Master/ProductView.axaml |
| VendorView | Forms/Master/VendorView.axaml |
| DepartmentView | Forms/Master/DepartmentView.axaml |
| CreditCardView | Forms/Master/CreditCardView.axaml |
| PriceChangeView | Forms/Master/PriceChangeView.axaml |
| OpnameView | Forms/Inventory/OpnameView.axaml |
| StockOutView | Forms/Inventory/StockOutView.axaml |
| TransferView | Forms/Inventory/TransferView.axaml |

### Default (32px rows) -- no class needed, this is the base:

- All POS forms (SaleView, PaymentWindow, ShiftView, CalculatorDialogWindow)
- All Purchasing forms
- All Accounting forms
- All Bank forms
- LoginView, MainMenuView
- All Admin forms
- All Shared dialogs (InputDialogWindow, MsgBoxWindow, WholesaleTierDialog)

### Comfortable (40px) -- not applied to any current form (reserved for future touch-only/customer-facing)

### Acceptance Criteria

- [x] Report DataGrids visibly denser than Sale DataGrid
- [x] Sale screen rows remain at 32px
- [x] All Default-density forms verified at 1024x768 viewport without horizontal/vertical scroll on primary content area. Forms to verify: SaleView, JournalView, PurchaseOrderView, GoodsReceiptView, PurchaseInvoiceView, AccountsView, CashReceiptView, CashDisbursementView, BankView, BankGiroView, ShiftView, PaymentWindow
- [x] `dotnet build` clean

---

## P5d -- Full Sweep Verification

### Process

For each of the 40 AXAML form files, verify:
1. No `StaticResource` for color/brush keys (all must be `DynamicResource`)
2. No hardcoded hex colors in AXAML attributes
3. `Background`, `Foreground`, `BorderBrush` all reference design system tokens
4. Font families reference `InterFont` or `JetBrainsMonoFont` resources

### Verification Commands

```bash
# Should return 0 results (no hardcoded colors in forms)
grep -rn 'Background="#\|Foreground="#\|BorderBrush="#\|Fill="#\|Stroke="#' Forms/ --include="*.axaml"

# Should return 0 results (no StaticResource for colors)
grep -rn 'StaticResource.*Brush\|StaticResource.*Color' Forms/ --include="*.axaml"

# Should return 0 results (no ThemeConstants in any .cs file)
grep -rn 'ThemeConstants\.' --include="*.cs"
```

### Acceptance Criteria

- [x] All 3 grep commands return 0 results
- [x] `dotnet build` clean
- [x] Automated XAML parse test runs each form's `InitializeComponent()` in headless Avalonia under both Dark and Light variants (build cleanly = pass)
- [x] Manual visual smoke samples 8 representative forms covering all subdirectories (POS/SaleView, Master/ProductView, Purchasing/PurchaseOrderView, Inventory/OpnameView, Accounting/JournalView, Reports/SalesReportView, Bank/BankView, Admin/UserView)

---

## P6 -- Sync Chrome (Status Bar Badge)

### State Machine (per FOUNDATIONS.md)

| State | Badge Class | Dot | Text |
|-------|-------------|-----|------|
| Online, sync recent | `ok` | green dot | "Online . Sync {X}d lalu" |
| Online, sync overdue (>5min) | `warn` | amber dot | "Sync tertunda" |
| Offline, can transact | `warn` | no dot | "Offline . transaksi tersimpan lokal" |
| Offline, cannot transact | `err` | red dot | "Tidak dapat memproses" |
| Sync running | spinner | animated | "Menyinkronkan..." |

### Implementation

1. New `Infrastructure/SyncStatusService.cs`:
   - Enum: `SyncState { OnlineSynced, OnlineOverdue, OfflineOk, OfflineError, Syncing }`
   - Expose `CurrentState` observable property
   - Wire to existing `Kasir.Core/Sync/SyncEngine` status events

2. `ShellWindow.axaml` status bar:
   - 36px bottom-docked panel
   - Left: sync badge (styled Border with TextBlock + dot)
   - Right: register ID, user name, clock
   - Badge click -> popover with last-sync time, queued count, "Sync sekarang" button

3. Hint bar (below title, above content):
   - Context-sensitive F-key hints per KEYBOARD.md
   - `Bg2Brush` background, `FgSecondaryBrush` text

### Acceptance Criteria

- [x] Badge shows correct state for each SyncState enum value
- [x] Badge color follows theme (ok=Success, warn=Warning, err=Danger)
- [x] Click opens popover
- [x] Hint bar shows F-key shortcuts for current active view
- [x] `dotnet build` clean

---

## P7 -- Regression Tests

### New Test Project: `Kasir.Avalonia.Tests`

```bash
cd ~/Code/kasir-worktrees/ds-v2
dotnet new nunit -n Kasir.Avalonia.Tests
cd Kasir.Avalonia.Tests
dotnet add reference ../Kasir.Avalonia/Kasir.Avalonia.csproj
dotnet add package Avalonia.Headless.NUnit
dotnet add package FluentAssertions
```

Add to solution:
```bash
cd ~/Code/kasir-worktrees/ds-v2
dotnet sln Kasir.sln add Kasir.Avalonia.Tests/Kasir.Avalonia.Tests.csproj
```

### Test: 20 Tokens Resolve in Both Variants

```csharp
[AvaloniaTest]
public class ThemeTokenTests
{
    private static readonly string[] TokenKeys = new[]
    {
        "Bg0Brush", "Bg1Brush", "Bg2Brush", "BgHoverBrush", "BgSelectedBrush",
        "AccentBgBrush", "BorderSubtleBrush", "BorderStrongBrush",
        "FgPrimaryBrush", "FgSecondaryBrush", "FgDimBrush", "FgNumericBrush",
        "FgOnBrandBrush", "BrandBrush", "BrandStrongBrush", "BrandSoftBrush",
        "SuccessBrush", "WarningBrush", "DangerBrush", "FocusRingBrush"
    };

    [Test]
    public void AllTokensResolveInDarkVariant() { /* set Dark, assert all 20 non-null */ }

    [Test]
    public void AllTokensResolveInLightVariant() { /* set Light, assert all 20 non-null */ }

    [Test]
    public void DarkAndLightValuesAreDifferent() { /* for each token, dark != light */ }
}
```

### Test: Theme Toggle

```csharp
[Test]
public void ThemeServiceTogglesVariant() { /* toggle, assert variant flipped, toggle back */ }

[Test]
public void ThemeServicePersistsPreference() { /* toggle, read file, assert matches */ }
```

### Test: StockColorConverter Dynamic Resolution

```csharp
[Test]
public void StockColorConverterUsesThemeResources() { /* set dark, convert, set light, convert, assert different */ }
```

### Acceptance Criteria

- [x] `dotnet test Kasir.Avalonia.Tests` passes (6+ tests)
- [x] Token resolution test covers all 20 brush keys in both variants
- [x] `dotnet test Kasir.Core.Tests` still passes (247+ tests, no regression)

---

## P8 -- PR Open

### Prerequisites

```bash
cd ~/Code/kasir-worktrees/ds-v2

# Final verification
dotnet build
dotnet test Kasir.Core.Tests
dotnet test Kasir.Avalonia.Tests

# Push
git push -u origin feat/design-system-v2
```

### Wait for CI

```bash
# Check CI status
gh run list --branch feat/design-system-v2 --limit 3
# Wait for green check
gh run watch
```

### Create PR

```bash
gh pr create \
  --title "feat: design system v2 — retail POS theme with dark/light toggle" \
  --body "$(cat <<'EOF'
## Summary

Replaces the terminal-CRT green aesthetic with the v2 retail-POS design system.

- **20 semantic color tokens** in Dark + Light variants (teal-green brand accent, neutral surfaces)
- **Live theme toggle** via `Ctrl+Shift+L` or title bar button, persisted across restart
- **Inter** (UI, via Avalonia.Fonts.Inter NuGet) + **JetBrains Mono** (numerics, bundled TTF)
- **Density classes**: Compact 28px (Reports/Master), Default 32px (POS/general), Comfortable 40px (reserved)
- **Lucide.Avalonia** for native Lucide iconography
- **Sync chrome** status bar badge with 5-state machine (Online/Overdue/Offline/Error/Syncing)
- **All 40 forms** swept for hardcoded colors, migrated to `DynamicResource`
- **SaleView footer** refactored from fixed px to token-bound heights
- **StockColorConverter** fixed: resolves brushes dynamically per call (was frozen at startup)
- **6+ regression tests** in new `Kasir.Avalonia.Tests` project (token resolution in both variants, toggle persistence)

No database or schema changes. Revert = revert this PR.

## Test Plan

- [ ] `dotnet build` clean (warnings as errors)
- [ ] `dotnet test Kasir.Core.Tests` — 247+ passing
- [ ] `dotnet test Kasir.Avalonia.Tests` — 6+ passing (token resolution, toggle, converter)
- [ ] Visual smoke: each form area renders in Dark theme
- [ ] Visual smoke: each form area renders in Light theme
- [ ] `Ctrl+Shift+L` flips entire UI live
- [ ] Theme persists across app restart
- [ ] Sale screen: 10 items fit, brand-color total
- [ ] Report DataGrids use compact density (28px rows)
- [ ] Status bar sync badge shows correct state text in Indonesian
- [ ] CI green on all 3 platforms (windows/macos/ubuntu)
EOF
)"
```

### Acceptance Criteria

- [x] PR created with descriptive title and body
- [x] CI passes on all platforms
- [x] PR is reviewable commit-by-commit (one commit per phase)

---

## Verification Checklist

| Check | Command | Expected |
|-------|---------|----------|
| Build | `dotnet build` | 0 errors, 0 warnings |
| Core tests | `dotnet test Kasir.Core.Tests` | 247+ passing |
| Avalonia tests | `dotnet test Kasir.Avalonia.Tests` | 6+ passing |
| No hardcoded colors (CS) | `grep -rn "ThemeConstants\.\|Colors\.\|Brush\.Parse" --include="*.cs" Kasir.Avalonia/` | 0 results |
| No hardcoded colors (AXAML) | `grep -rn 'Background="#' --include="*.axaml" Kasir.Avalonia/Forms/` | 0 results |
| No StaticResource brushes/colors | `grep -rn 'StaticResource.*Brush\|StaticResource.*Color' --include="*.axaml" Kasir.Avalonia/Forms/` | 0 results |
| Theme toggle | Manual: `Ctrl+Shift+L` | UI flips between Dark/Light |
| Density | Manual: open SalesReport vs Sale | Report rows visibly denser |
| Sync badge | Manual: check status bar | Indonesian text, correct badge color |
| Font rendering | Manual: check DataGrid numbers | JetBrains Mono, right-aligned |

---

## Risks

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Density 22->32 breaks fixed-height layouts | HIGH | Forms overflow on old 1024x768 screens | Compact class on dense forms; SaleView footer refactored to Auto/token heights |
| StockColorConverter returns stale brush on theme switch | HIGH (confirmed bug) | Wrong colors after toggle | P5a: resolve dynamically via `Application.Current.FindResource()` |
| `Avalonia.Fonts.Inter` NuGet font name mismatch | MEDIUM | Inter doesn't render, falls back to system | Verify exact font family string; Avalonia 12.0.1 registers as "Inter" |
| `Avalonia.Headless.NUnit` incompatible with .NET 10 | MEDIUM | Can't run Avalonia tests | Check NuGet compatibility; fallback to manual headless app builder |
| Theme toggle race on startup | LOW | Flash of wrong theme | Apply `RequestedThemeVariant` in `App.OnFrameworkInitializationCompleted` before any window shown |
| Large PR review fatigue | LOW | Missed issues in review | Phase-per-commit structure; self-review each commit; run full verification before PR |
| Lucide.Avalonia version conflict with Avalonia 12 | LOW | Build failure | v0.2.5 requires Avalonia >= 11.2.2; verify compatibility with Avalonia 12 on nuget.org before adding |

---

## Rollback Strategy

- **No schema changes, no data migrations** -- purely UI/theming
- **Single PR**: `git revert <merge-commit>` or close PR and delete branch
- **Worktree isolation**: main branch untouched during development
- **Verification gate**: PR only opened after CI green on all 3 platforms

---

## Out of Scope

| Item | Reason |
|------|--------|
| Real logo/wordmark asset | Per user: skipped |
| Customer-facing display | Per user: skipped |
| Audio cues implementation | Reserve sound event IDs only; actual playback deferred |
| Print stylesheet cosmetic update | Existing ReceiptBuilder works; cosmetic update is separate work |
| Lucide icon audit | Lucide.Avalonia provides native Lucide icons; full icon-by-icon audit against design bundle deferred |
| Discount engine UI | Explicitly deferred per project decision |
| Comfortable density activation | Token defined but no forms use it yet (future touch-only registers) |
