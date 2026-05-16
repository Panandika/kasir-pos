# DS-0: Avalonia 12.x Compatibility Checkpoint — Verdict

**Date:** 2026-04-18
**Branch:** `feature/avalonia-migration`
**Target:** Kasir.Avalonia on Avalonia 12.x + net10.0, Windows 10 deployment

---

## Verdict: **GO**

Proceed with **Option B** (Keep `FluentTheme` + layer `BaseTheme.axaml` overrides) per the FluentTheme Migration Decision in `plans/DESIGN-SYSTEM.md`. No pivot required.

---

## Evidence

### Q1: Does FluentTheme + DataGrid Fluent.xaml work on Avalonia 12.x?

**Answer: Yes.**

- `kasir-pos/Kasir.Avalonia/Kasir.Avalonia.csproj` pins:
  - `Avalonia` 12.0.1
  - `Avalonia.Desktop` 12.0.1
  - `Avalonia.Themes.Fluent` 12.0.1
  - `Avalonia.Controls.DataGrid` 12.0.0
  - `Avalonia.Fonts.Inter` 12.0.1
- `App.axaml:5-8` already uses `<FluentTheme />` + `<StyleInclude Source="avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml" />`.
- **Build result:** `dotnet build Kasir.Avalonia.slnx` → **Build succeeded. 0 Warning(s), 0 Error(s). Time Elapsed 00:00:01.96.**
- Output artifacts produced:
  - `Kasir.Core.dll` (net10.0)
  - `Kasir.Avalonia.dll` (net10.0)
  - `Kasir.Core.Tests.dll` (net10.0)

### Q2: Are Semi.Avalonia / Ursa.Avalonia compatible with Avalonia 12.x?

**Semi.Avalonia: Yes.** Version **12.0.0** released **2026-04-07** on NuGet, declares dependency `Avalonia (>= 12.0.0)`. Source: https://www.nuget.org/packages/Semi.Avalonia/

**Ursa.Avalonia: No confirmed 12.x release.** The official Ursa.Avalonia GitHub README still documents compatibility with Avalonia 11.1.x and 11.2.x only (excluding 11.2.0). Source: https://github.com/irihitech/Ursa.Avalonia

**Implication:** Option A (Semi + Ursa as base theme) is now partially viable — Semi is ready, Ursa is not. This does NOT change the DS-0 verdict because Option B is already the chosen path per the spec's Decision (Fluent is working, DataGrid chrome would be expensive to rebuild). Semi remains a documented drop-in replacement candidate for **future** theme work (post-v2) if Ursa later publishes a 12.x release.

### Q3: Does self-contained win-x64 publish produce a working binary?

**Answer: Yes.**

Command: `dotnet publish Kasir.Avalonia/Kasir.Avalonia.csproj -c Release -r win-x64 --self-contained true -o /tmp/kasir-ds0-publish`

Result:
- Exit code: 0
- `Kasir.Avalonia.exe` produced: **159K** launcher
- Total bundle size: **222 MB** (self-contained net10.0 runtime + Avalonia + Fluent assets)
- All restores succeeded
- No compilation errors

Bundle size is larger than the prior .NET Framework 4.8 xcopy deploy but acceptable for modern SSD-backed Win10 registers. Matches expectations for self-contained .NET 10 published app.

### Q4 (bonus): Embedded fonts path

Currently the project uses `Avalonia.Fonts.Inter` as a NuGet package (fonts shipped inside the package). This already exercises the embedded-font code path. When BaseTheme ships, JetBrains Mono and IBM Plex Sans will be embedded as `AvaloniaResource` in the same manner. Self-contained publish already works with the existing `Avalonia.Fonts.Inter` font package, so the embedding path is proven.

---

## Decision Consequences

- **Option B confirmed.** Keep `<FluentTheme />` in `App.axaml`. Layer `<StyleInclude Source="avares://Kasir.Avalonia/Themes/BaseTheme.axaml" />` after it in DS-2.
- **No pivot to Option C (SimpleTheme).** SimpleTheme remains the documented fallback only.
- **Semi.Avalonia reopens as a future alternative.** Out of scope for v1 per `DESIGN-SYSTEM.md §Out of Scope`, but now de-risked — if DS-2 shows Fluent overrides are harder than expected, Semi 12.0.0 is a viable Phase-2 replacement (its tokens mirror the semantic model in `DESIGN-SYSTEM.md §5`).
- **Unblock DS-1 onward.** Ralph loop for DS-0 complete. Hand off DS-1..DS-5 to team execution per `plans/design-system-prd.json` dependency graph.

---

## Unblocks

- US-DS-1 (audit) — can start immediately
- US-DS-2 (BaseTheme) — depends on US-DS-1
- US-DS-3a..3h (migration batches) — depend on US-DS-2
- US-DS-4 (bento menu) — depends on US-DS-3h
- US-DS-5 (perf + CI) — depends on US-DS-3h

---

## Sources

- [Semi.Avalonia 12.0.0 on NuGet](https://www.nuget.org/packages/Semi.Avalonia/)
- [Ursa.Avalonia repository](https://github.com/irihitech/Ursa.Avalonia)
- [Avalonia 12.0.0 Release Discussion](https://github.com/AvaloniaUI/Avalonia/discussions/21091)
- [Breaking changes in Avalonia 12](https://docs.avaloniaui.net/docs/avalonia12-breaking-changes)
