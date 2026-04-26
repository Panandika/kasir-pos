# Plan: Release-Please Automation for kasir-pos

**Created:** 2026-04-18  
**Status:** Draft  
**Scope:** CI/CD — automated versioning, tagging, and GitHub Releases

---

## Context & Current State

| Item | Current |
|------|---------|
| Release trigger | Manual `git tag v2.0.0-avalonia && git push origin <tag>` |
| Release builder | `release.yml` → builds legacy WinForms (`Kasir.sln` via msbuild) ⚠️ |
| CI builder | `build.yml` → builds Avalonia (`Kasir.Avalonia.slnx` via dotnet) |
| Version in .csproj | CalVer `2026.04.1` |
| Version in tags | SemVer-ish `v2.0.0-avalonia` |
| Release notes | Manual `whatsnew.txt` (Indonesian, hand-written) |
| Commit style | Mostly conventional (`feat:`, `fix:`, `refactor:`) + custom `ds-N:` |

### Key Problems Found
1. `release.yml` still builds the **legacy WinForms** app — it should build Avalonia
2. Version is inconsistent between tags (`v2.0.0`) and .csproj (`2026.04.1`)
3. `ds-N:` commits (design system) are not conventional — Release-Please ignores them
4. `whatsnew.txt` is a manual burden that will go stale

---

## Requirements

1. Pushing commits to `main` should automatically create/update a "Release PR"
2. Merging the Release PR auto-tags and triggers the real release build
3. CHANGELOG.md auto-generated from commit history
4. `ds-N:` design system commits appear in changelog under their own section
5. release.yml must build **Avalonia** (`Kasir.Avalonia.slnx`), not legacy WinForms
6. Version source of truth: SemVer tag (Release-Please manages it)

---

## Acceptance Criteria

- [ ] Pushing a `feat:` commit to `main` opens/updates a Release PR bumping minor version
- [ ] Pushing a `fix:` commit to `main` opens/updates a Release PR bumping patch version
- [ ] Merging the Release PR creates a `v*` tag and triggers `release.yml`
- [ ] `release.yml` publishes `kasir-<version>.zip` built from `Kasir.Avalonia.slnx`
- [ ] GitHub Release page shows bilingual install instructions + generated CHANGELOG
- [ ] `ds-N:` commits appear in CHANGELOG under "Design System" section
- [ ] No manual `git tag` step required

---

## Implementation Steps

### Step 1 — Fix `release.yml` to build Avalonia (not WinForms)

**File:** `.github/workflows/release.yml`

Replace the msbuild pipeline with dotnet:
- Remove: `Setup MSBuild`, `msbuild Kasir.sln`, `Setup VSTest`, `vstest.console.exe`
- Add: `dotnet build Kasir.Avalonia.slnx -c Release`
- Add: `dotnet test Kasir.Core.Tests/Kasir.Core.Tests.csproj -c Release`  
- Add: `dotnet publish Kasir.Avalonia/Kasir.Avalonia.csproj -c Release -r win-x64 --self-contained -o publish/win-x64`
- Change runner: `windows-latest` → `windows-latest` (keep, win-x64 publish needs it)
- Remove: AssemblyInfo patching (WinForms-only, no longer needed)
- Keep: checksum generation, HMAC signing, ZIP creation, GitHub Release step

### Step 2 — Add `release-please-config.json`

**File:** `release-please-config.json` (repo root)

```json
{
  "packages": {
    ".": {
      "release-type": "simple",
      "package-name": "kasir",
      "changelog-sections": [
        { "type": "feat",     "section": "Features" },
        { "type": "fix",      "section": "Bug Fixes" },
        { "type": "refactor", "section": "Refactoring" },
        { "type": "ds",       "section": "Design System", "hidden": false },
        { "type": "perf",     "section": "Performance" },
        { "type": "chore",    "section": "Maintenance", "hidden": true }
      ]
    }
  }
}
```

Note: `ds` is registered as a known type so `ds-3a:`, `ds-4:` style commits appear in changelog.

### Step 3 — Add `.release-please-manifest.json`

**File:** `.release-please-manifest.json` (repo root)

```json
{
  ".": "2.0.0"
}
```

Seeds the current version so Release-Please knows where to start. Next release will be `2.0.1` (fix) or `2.1.0` (feat).

### Step 4 — Add `release-please.yml` workflow

**File:** `.github/workflows/release-please.yml`

```yaml
name: Release Please

on:
  push:
    branches: [main]

permissions:
  contents: write
  pull-requests: write

jobs:
  release-please:
    runs-on: ubuntu-latest
    steps:
      - uses: googleapis/release-please-action@v4
        id: release
        with:
          config-file: release-please-config.json
          manifest-file: .release-please-manifest.json
```

This workflow:
- On every push to `main`: creates/updates a "Release PR" titled `chore(main): release 2.x.x`
- When the Release PR is merged: creates the `v2.x.x` tag → triggers `release.yml`

### Step 5 — Update `build.yml` branch list

**File:** `.github/workflows/build.yml` line 5

```yaml
# Before:
branches: [ main, feature/avalonia-migration ]
# After (once branch is merged):
branches: [ main ]
```

### Step 6 — Retire `whatsnew.txt`

- Delete `whatsnew.txt` from repo root
- Update `release.yml`: remove the `Copy whatsnew.txt` step (replaced by generated CHANGELOG)
- The GitHub Release body (bilingual install instructions, added 2026-04-18) already covers user-facing notes

### Step 7 — Document commit convention in README

Add a short section to `README.md`:

```
## Release Process
Commits to `main` using conventional prefixes trigger automated releases:
- `feat: ...` → minor version bump
- `fix: ...` → patch version bump  
- `ds-N: ...` → design system (patch bump, appears in changelog)
- `chore: ...` → no release
Merging the auto-generated Release PR publishes to GitHub Releases.
```

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| `ds-N:` commits cause minor bumps (they have no `feat`/`fix` type) | Register `ds` as a custom type in changelog-sections; it defaults to patch |
| HMAC signing secret not set in new CI | Keep existing HMAC logic in release.yml; secret already configured |
| Old `whatsnew.txt` steps break release | Remove the step in Step 6 before first Release-Please tag |
| release.yml still references AssemblyInfo (WinForms path) | Step 1 removes that entirely |
| First Release PR shows all historical commits | Seed manifest at `2.0.0`; history before that is cleanly cut |
| `feature/avalonia-migration` not yet merged | Steps 4-6 can only go live after merge to `main` |

---

## Execution Order

```
Step 1 (fix release.yml — Avalonia build)
Step 2 + 3 (add release-please config files)     ← can be parallel
Step 4 (add release-please.yml workflow)
── merge feature/avalonia-migration to main ──
Step 5 (clean up build.yml branch list)
Step 6 (retire whatsnew.txt)
Step 7 (update README)
── push to main → Release-Please creates first PR ──
```

---

## Verification Steps

1. Push a `fix:` commit to `main` → confirm Release PR appears within 60s
2. Check Release PR title: `chore(main): release 2.0.1`
3. Merge the PR → confirm `v2.0.1` tag created + `release.yml` triggered
4. Confirm `release.yml` builds Avalonia (not WinForms) — check Actions log for `dotnet publish`
5. Download the release ZIP → run `Kasir.Avalonia.exe` → confirm it starts
6. Confirm CHANGELOG.md updated with the fix commit
7. Push a `ds-3x:` commit → confirm it appears in next Release PR CHANGELOG under "Design System"

---

## ADR: Release-Please over semantic-release or manual tags

**Decision:** Use Release-Please (Google) with `release-type: simple`

**Drivers:**
1. Solo dev — minimal ceremony, one human step (PR merge) over zero-touch full automation
2. Non-npm project — `release-type: simple` works without package.json
3. Commit discipline already present — conventional commits are already the norm
4. `ds-N:` custom prefix — configurable changelog sections handle this cleanly

**Alternatives considered:**
- **semantic-release**: Fully automated but requires stricter commit discipline and complex plugin config for .NET; every commit auto-releases (too aggressive for solo dev)
- **Changesets**: Better for monorepos with multiple packages; overkill for single-register app
- **Keep manual tags**: Zero setup, but blocks automation of install notes and changelog

**Why Release-Please:** One extra human step (merge the PR) gives full control over when to ship. The Release PR accumulates all changes and shows the version bump before release — perfect for a retail POS where releases need deliberate timing.

**Consequences:** 
- Must use conventional commit prefixes going forward (`feat:`, `fix:`, `ds-N:`)
- `ds-N:` commits need the colon format (`ds-3a: ...`) to be parsed correctly
- `whatsnew.txt` becomes obsolete — CHANGELOG.md takes over

**Follow-ups:**
- Consider adding commit-msg hook (husky/lefthook) to enforce conventional format locally
- Consider `version.txt` update step in release.yml to keep a human-readable version file in the ZIP
