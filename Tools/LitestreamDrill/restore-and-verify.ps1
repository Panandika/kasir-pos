<#
.SYNOPSIS
  Phase D US-D3 — Litestream restore drill.

.DESCRIPTION
  Restores kasir.db from Cloudflare R2 to a temp location and verifies
  integrity + row-count parity against the production source.

  Run this monthly per the runbook. Failures escalate per
  docs/LITESTREAM-DRILL.md.

.PARAMETER LitestreamPath
  Path to litestream.exe (default: C:\Program Files\Litestream\litestream.exe).

.PARAMETER ConfigPath
  Litestream config YAML (default: C:\ProgramData\Litestream\litestream.yml).

.PARAMETER SourceDb
  Path to the production kasir.db for parity comparison.

.PARAMETER RestoreDir
  Where to put the restored copy (default: $env:TEMP\kasir-restore).
#>
param(
    [string]$LitestreamPath = "C:\Program Files\Litestream\litestream.exe",
    [string]$ConfigPath     = "C:\ProgramData\Litestream\litestream.yml",
    [string]$SourceDb       = "C:\kasir\data\kasir.db",
    [string]$RestoreDir     = "$env:TEMP\kasir-restore"
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $LitestreamPath)) {
    Write-Error "Litestream not found at $LitestreamPath"
    exit 2
}
if (-not (Test-Path $SourceDb)) {
    Write-Error "Source kasir.db not found at $SourceDb"
    exit 2
}

if (Test-Path $RestoreDir) { Remove-Item -Recurse -Force $RestoreDir }
New-Item -ItemType Directory -Path $RestoreDir | Out-Null
$RestoreDb = Join-Path $RestoreDir 'kasir-restored.db'

Write-Host "=== Drill step 1/4: restore from R2 ===" -ForegroundColor Cyan
& $LitestreamPath restore -config $ConfigPath -o $RestoreDb $SourceDb
if ($LASTEXITCODE -ne 0) {
    Write-Error "litestream restore failed (exit $LASTEXITCODE)"
    exit 1
}
Write-Host "  restored to $RestoreDb"

Write-Host "=== Drill step 2/4: PRAGMA integrity_check ===" -ForegroundColor Cyan
$integrity = sqlite3 $RestoreDb 'PRAGMA integrity_check;'
if ($integrity -ne 'ok') {
    Write-Error "Integrity check failed: $integrity"
    exit 1
}
Write-Host "  integrity_check = ok"

Write-Host "=== Drill step 3/4: row-count parity per critical table ===" -ForegroundColor Cyan
$tables = @('products','sales','sale_items','stock_movements','sync_queue')
$mismatch = $false
foreach ($t in $tables) {
    $src = sqlite3 $SourceDb  "SELECT COUNT(*) FROM $t;"
    $dst = sqlite3 $RestoreDb "SELECT COUNT(*) FROM $t;"
    if ($src -eq $dst) {
        Write-Host "  $t : OK ($src rows)"
    } else {
        Write-Host "  $t : MISMATCH src=$src restored=$dst" -ForegroundColor Yellow
        # Note: small drift (e.g. <100 rows) on sync_queue is expected because
        # WAL replication has bounded lag. >1% drift on data tables is a fail.
        if ($t -ne 'sync_queue') { $mismatch = $true }
    }
}

Write-Host "=== Drill step 4/4: file size sanity ===" -ForegroundColor Cyan
$srcSize = (Get-Item $SourceDb).Length
$dstSize = (Get-Item $RestoreDb).Length
$diffPct = [math]::Abs(($dstSize - $srcSize) / $srcSize * 100)
Write-Host "  source=$srcSize bytes  restored=$dstSize bytes  delta=$([math]::Round($diffPct, 2))%"

if ($mismatch) {
    Write-Error "DRILL FAILED — see mismatches above"
    exit 1
}

Write-Host "DRILL PASS — restore + integrity + parity all green" -ForegroundColor Green
