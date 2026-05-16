# RASIO/YONICO POS — Avalonia Design System Plan v2

**Status:** Draft v2 (revised per Architect + Critic feedback)
**Date:** 2026-04-18
**Scope:** Design system for Kasir.Avalonia — centralize 482 hardcoded values across 39 AXAML files, establish shared theme infrastructure, enforce build-gated quality.
**Runtime (locked):** Kasir.Core = `net8.0`, Kasir.Avalonia = `net10.0` + Avalonia 12.x.

---

## RALPLAN-DR Summary

### Principles (ranked)

1. **Keyboard-first.** Every action reachable without a mouse.
2. **Density-first.** Maximize information per screen; touch is a non-goal.
3. **Performance-first.** Every component has a frame budget; effects that break it are cut.
4. **Legibility under bad conditions.** High contrast, no thin fonts, state not color-alone.
5. **Consistency over cleverness.** Same key = same thing in every form.

### Decision Drivers (top 3)

1. **Migration surface is the bottleneck.** 238 hardcoded `#00dc00` + 244 hardcoded `FontFamily="Consolas..."` across 39 files = 482 inline values that must be centralized form-by-form. This is the actual work.
2. **Avalonia 12.x is pre-release.** Semi.Avalonia and Ursa.Avalonia have zero confirmed compatibility. Theme choice must not depend on unverified third-party packages.
3. **Muscle memory is already preserved.** The current green-on-black palette and Menu/MenuItem tree with `_` access keys match the 30-year FoxPro/Harbour UX. The design system rationalizes this into tokens; it does not reinvent it.

### Options Considered

**Option A: Semi.Avalonia + Ursa.Avalonia as base theme.**
Dense, enterprise-leaning, good DataGrid. However: zero references in the codebase today, zero confirmed Avalonia 12.x compatibility (Semi tracks Avalonia 11.x stable). Adopting an unverified third-party theme as the foundation of a POS system that must ship on pre-release Avalonia is an unacceptable risk. If Semi publishes a verified 12.x-compatible release before DS work completes, it can be reconsidered as a drop-in replacement for hand-built ControlTemplates — the token layer is the same either way.

**Option B (chosen): Keep FluentTheme + layer semantic overrides on top.**
FluentTheme is already in `App.axaml` and working. The DataGrid depends on `Avalonia.Controls.DataGrid/Themes/Fluent.xaml`. Layering `BaseTheme.axaml` resource dictionaries on top of Fluent preserves all existing control styling while replacing hardcoded values with semantic tokens. Risk: Fluent's spacing defaults are looser than ideal for dense POS — mitigated by explicit density-token overrides on every control class used in forms.

**Option C: SimpleTheme + hand-built ControlTemplates from scratch.**
Maximum control, no Fluent baggage. However: requires rebuilding ControlTemplates for TextBox, ComboBox, DataGrid, Menu, MenuItem, Button, and every other control from scratch. For 39 forms on a solo dev, this is 2-4 weeks of template work before any form migration begins. The DataGrid alone needs frozen columns, sort indicators, and inline edit — all of which Fluent's DataGrid theme already provides. This option is valid if FluentTheme is later found incompatible with Avalonia 12.x, making it the documented fallback, not the default.

---

## User Stories

### DS-0: Avalonia 12.x Compatibility Checkpoint (BLOCKING GATE)

Before any theme work begins, answer three questions with documented evidence:

1. Does `FluentTheme` + `Avalonia.Controls.DataGrid/Themes/Fluent.xaml` work on Avalonia 12.x? (Current `App.axaml` already uses this — verify it still builds and renders after any 12.x preview updates.)
2. Are Semi.Avalonia / Ursa.Avalonia compatible with Avalonia 12.x? Check GitHub issues, release notes, NuGet. Document findings.
3. Does `dotnet publish -c Release -r win-x64 --self-contained` produce a working binary with embedded fonts on Avalonia 12.x?

**Outcome:** GO (proceed with Option B) or PIVOT (fall back to Option C with documented justification).

**Acceptance criteria:**
- `dotnet build Kasir.Avalonia.slnx` passes on Avalonia 12.x
- `dotnet publish -r win-x64 --self-contained` produces a runnable exe
- Findings documented in `plans/DS-0-COMPATIBILITY.md` with go/no-go verdict
- Semi/Ursa compatibility status recorded with evidence (links to issues/releases)

### DS-1: Audit All 39 AXAML Files — Catalog Hardcoded Values and Patterns

Systematic audit of every `.axaml` file in `Forms/`. For each file, catalog:
- Hardcoded color values (e.g., `#00dc00`, `#002800`, `#001400`, `#004400`, `#008800`, `#ff9900`)
- Hardcoded `FontFamily=` declarations
- Hardcoded `FontSize=` values
- Recurring layout patterns (status bars, form headers, action button rows)
- Missing `CompiledBindings` / `x:DataType`

Output: `plans/DS-1-AUDIT.md` with per-file inventory and a consolidated token mapping table (current hardcoded value -> proposed semantic token).

**Acceptance criteria:**
- `grep -rc '#[0-9a-fA-F]\{6\}' Forms/**/*.axaml` output fully accounted for in audit
- Every unique color value mapped to a semantic token or explicitly marked "remove"
- `grep -c 'FontFamily=' Forms/**/*.axaml` count matches audit total (currently 244)
- `grep -c '#00dc00' Forms/**/*.axaml` count matches audit total (currently 238)

### DS-2: Create BaseTheme.axaml — Shared Styles and Semantic Tokens

Based on DS-1 audit findings, create `Kasir.Avalonia/Themes/BaseTheme.axaml` containing:
- All semantic color tokens as `<Color>` and `<SolidColorBrush>` resources
- Font family resources (UI font, numeric font)
- Font size resources matching density tokens
- Shared `<Style>` definitions for recurring patterns found in audit (TextBlock, TextBox, Button, Menu/MenuItem, DataGrid, status bars)
- Density token resources (row height, control height, padding)

FluentTheme stays in `App.axaml`. BaseTheme.axaml is added as a second `<StyleInclude>` after FluentTheme, overriding Fluent defaults with the green-on-black terminal palette.

Update `ThemeConstants.cs` to reference the same token values (for the 1 code-behind file that uses it).

**Acceptance criteria:**
- `dotnet build Kasir.Avalonia.slnx` passes
- `BaseTheme.axaml` contains resources for all tokens identified in DS-1 audit
- `App.axaml` includes `<FluentTheme />` + `<StyleInclude Source="...BaseTheme.axaml" />`
- `ThemeConstants.cs` color values match BaseTheme.axaml token values

### DS-3: Migration Pass — Centralize Hardcoded Values (by directory batch)

Form-by-form rewrite: replace all hardcoded `Foreground="#00dc00"`, `FontFamily="Consolas,..."`, `Background="#000000"`, etc. with `{DynamicResource TokenName}` references to BaseTheme.axaml tokens. Add `CompiledBindings="True"` and `x:DataType` to every view.

Batched by directory for independent shippability:

| Batch | Directory | Files | Est. hardcoded values |
|-------|-----------|-------|-----------------------|
| 3a | `Forms/Shared/` + `Forms/` (Login, MainMenu) | 4 | ~55 |
| 3b | `Forms/POS/` | 4 | ~55 |
| 3c | `Forms/Admin/` | 6 | ~45 |
| 3d | `Forms/Master/` | 5 | ~45 |
| 3e | `Forms/Purchasing/` | 4 | ~50 |
| 3f | `Forms/Accounting/` | 6 | ~55 |
| 3g | `Forms/Inventory/` | 3 | ~20 |
| 3h | `Forms/Bank/` + `Forms/Reports/` | 7 | ~60 |

**Acceptance criteria (per batch, grep-verifiable):**
- `grep -c '#00dc00' Forms/<dir>/*.axaml` returns 0
- `grep -c 'FontFamily="Consolas' Forms/<dir>/*.axaml` returns 0
- `grep -c 'CompiledBindings="True"' Forms/<dir>/*.axaml` equals file count in batch
- `dotnet build Kasir.Avalonia.slnx` passes after each batch
- App launches and the batch's forms render correctly (visual spot check)

**Totals after all batches:**
- Zero hardcoded `#00dc00` in `Forms/**/*.axaml`
- Zero hardcoded `FontFamily=` in `Forms/**/*.axaml`
- `CompiledBindings="True"` on every Window/UserControl

### DS-4: Main Menu Redesign — Bento Tiles + Shortcut Underlines (IN SCOPE per user override)

**Reviewer note:** Architect and Critic both flagged bento as a muscle-memory risk in v2. User explicitly overrode this decision. The bento layout is in scope. Muscle-memory is preserved via **structural invariants** below, not by keeping the menu bar.

**Scope:**
1. Replace `MainMenuView.axaml` Menu/MenuItem tree with a bento-tile grid that fills the screen area below the status bar.
2. Each top-level category (Transaksi, Master, Pembelian, Gudang, Akuntansi, Bank, Laporan, Admin) becomes a tile.
3. Tiles arranged in a deterministic grid whose visual order **matches the exact order of the current menu bar** (left-to-right, top-to-bottom reading order).
4. Tile activation opens the existing sub-menu (do NOT redesign sub-menus — they stay as dropdown/flyout or panel; only the top-level entry point changes).
5. Underline the shortcut letter on every tile and every sub-menu item (e.g., **T**ransaksi, **P**enjualan). Apply same underline treatment to all dropdown menus app-wide, not just main menu.
6. Migrate all hardcoded colors in `MainMenuView.axaml` to BaseTheme tokens.

**Muscle-memory invariants (non-negotiable):**
- Every Alt+letter shortcut from the current menu bar works identically on the new bento layout — no key changes, no new keys.
- Category order preserved exactly: Transaksi → Master → Pembelian → Gudang → Akuntansi → Bank → Laporan → Admin.
- Sub-menu structure and shortcut keys inside each category are untouched. Only the top-level opens a bento tile instead of a menu dropdown.
- Keyboard navigation: arrow keys move focus between tiles; Enter or the tile's shortcut letter opens its sub-menu; Esc returns to previous state.
- F10 focuses the main menu (preserved from current behavior).

**Acceptance criteria:**
- `MainMenuView.axaml` renders 8 tiles filling the main area, ordered Transaksi → Admin matching current menu bar order (visual spot check + code review).
- Every Alt+letter that worked before works after (manual test: Alt+T, Alt+M, Alt+P, Alt+G, Alt+A, Alt+B, Alt+L, Alt+D — adjust per actual access keys).
- Sub-menus unchanged — same items, same shortcuts (diff check against pre-change sub-menu AXAML).
- Arrow keys + Enter navigate tiles without mouse.
- Shortcut letters visibly underlined on all tiles AND on all sub-menu items app-wide.
- `grep -c '#00dc00' Forms/MainMenuView.axaml` returns 0.
- `grep -c 'TextDecorations.*Underline' Forms/**/*.axaml` > 0 on every file containing `MenuItem` with an access key (regression gate that underlines weren't missed).
- `dotnet build Kasir.Avalonia.slnx` passes.
- Perf: F10 → bento visible ≤ 100ms (logged via DS-5 instrumentation).

### DS-5: Performance Instrumentation and CI Gates

Instrument the 7 performance budgets from the spec as logged metrics. Each interaction emits PASS or FAIL per measurement, visible in application log.

Add CI grep-assertions:
- Zero `DropShadowEffect`, `BlurEffect`, `OpacityMask`, `Acrylic` in `Forms/**/*.axaml`
- `VirtualizingStackPanel` present in any list binding > 50 items
- Zero hardcoded `#00dc00` (regression gate for DS-3)
- Zero hardcoded `FontFamily="Consolas` (regression gate for DS-3)

**Acceptance criteria:**
- All 7 perf metrics emit PASS or FAIL per interaction in log output
- `grep -rc 'DropShadowEffect\|BlurEffect\|OpacityMask\|Acrylic' Forms/**/*.axaml` returns 0
- CI script/test exists that runs all grep assertions and fails the build on violation
- `dotnet build Kasir.Avalonia.slnx` passes

---

## Story Ordering

```
DS-0 (blocking gate) --> DS-1 (audit) --> DS-2 (BaseTheme) --> DS-3a..3h (migration batches) --> DS-4 (menu) --> DS-5 (perf + CI)
```

DS-0 must complete with GO verdict before any other story begins. DS-1 informs DS-2. DS-3 batches are sequential but independently shippable. DS-4 and DS-5 can run in parallel after DS-3.

---

## Palette Rationalization (honest framing)

The current codebase uses 13 ad-hoc colors in `ThemeConstants.cs`. The design system introduces 17 semantic tokens. This is **palette rationalization** — giving consistent names and roles to colors — not "muscle-memory preservation." Muscle memory is preserved by keeping the same core green-on-black aesthetic that cashiers have used for 30 years. The 4 new tokens (`--bg-hover`, `--bg-selected`, `--border-subtle`, `--accent`) are new semantic distinctions for UI consistency, not for preservation.

---

## FluentTheme Migration Decision

**Decision:** Keep `<FluentTheme />` in `App.axaml` and layer `BaseTheme.axaml` overrides on top.

**Why:** FluentTheme is already working (`App.axaml:6`). The DataGrid depends on `Avalonia.Controls.DataGrid/Themes/Fluent.xaml` (`App.axaml:7`) for frozen columns, sort indicators, and inline editing. Removing Fluent means rebuilding all DataGrid chrome from scratch — disproportionate effort for a solo dev. Fluent's looser spacing defaults are overridden by explicit density-token styles in BaseTheme.axaml targeting each control class.

**Fallback:** If DS-0 checkpoint reveals FluentTheme is broken on Avalonia 12.x, pivot to Option C (SimpleTheme + hand-built ControlTemplates) with updated timeline.

---

## Out of Scope (v1)

- Sub-menu redesign (only top-level main menu becomes bento; sub-menus stay as dropdowns)
- Semi.Avalonia / Ursa.Avalonia adoption (revisit if 12.x compatibility confirmed)
- Light theme, touchscreen layouts, localization beyond Indonesian
- Animation/motion beyond 150ms modal fade
- Screen-reader accessibility (v2)
- New custom components (CommandPalette, LookupField, etc.) — build only after migration pass proves the need

---

## Risks

| Risk | Mitigation |
|------|------------|
| Avalonia 12.x breaks FluentTheme or DataGrid theme | DS-0 checkpoint catches this before any work. Fallback: Option C. |
| 482 hardcoded values take longer than estimated | Batched by directory; each batch independently shippable. Can pause after any batch. |
| BaseTheme overrides conflict with Fluent defaults | Test each control class individually during DS-2. Fluent specificity is documented in Avalonia source. |
| Embedded fonts fail in self-contained publish | DS-0 checkpoint tests this explicitly. |
| Density overrides break DataGrid column sizing | Benchmark with 1000+ rows on target hardware during DS-2. |
| Bento menu disrupts cashier spatial muscle memory | Preserve exact category order, exact Alt+letter shortcuts, untouched sub-menus. Ship alongside 2-week parallel-run period so users can complain early. If rejected, revert is localized to `MainMenuView.axaml` — no other forms affected. |

---

## Changes from v1

1. **Inverted DS-2/DS-7 ordering.** Audit (DS-1) now comes before component building. Components are deferred to "out of scope" — build only after the migration pass proves the need.
2. **Bento main menu IN SCOPE (user override in iteration 3).** DS-4 redesigns `MainMenuView.axaml` as a bento-tile grid with underlined shortcut letters. Architect and Critic flagged muscle-memory risk in iteration 2; user explicitly overrode. Risk mitigated via structural invariants: exact category order, exact Alt+letter shortcuts, untouched sub-menus, localized revert path.
3. **Added DS-0 Avalonia 12.x compatibility checkpoint** as an early blocking gate answering FluentTheme, Semi/Ursa compatibility, and self-contained publish questions.
4. **All acceptance criteria are grep-verifiable or build-gated.** Every story has concrete `grep` commands or `dotnet build` checks as its definition of done.
5. **Palette rationalization honestly framed.** Acknowledged that going from 13 to 17 tokens is rationalization, not preservation. Core green-on-black is what preserves muscle memory.
6. **FluentTheme migration explicitly named.** Decision: keep Fluent, layer overrides. Justified by DataGrid dependency. Fallback to SimpleTheme documented.
7. **DS-3 is a "migration surface" story** budgeting the 238+244 hardcoded values as form-by-form rewrite batches (8 batches by directory), each independently shippable.
8. **Fair alternatives section.** Options A (Semi) and C (SimpleTheme) given 2-3 sentence treatments explaining why they fail this context, with documented conditions under which they could be reconsidered.
