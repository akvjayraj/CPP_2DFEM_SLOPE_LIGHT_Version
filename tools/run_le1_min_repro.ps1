#requires -Version 5.1
<#
.SYNOPSIS
  Minimal 2-element T3 patch-test smoke runner. Exits 0 on PASS, 1 on FAIL.

.DESCRIPTION
  Runs `library/solver/test/le1_min_repro.dat` through `p610_t3_elastoplastic.exe`
  and asserts that every element returns the constant-stress patch result
  (sxx=-100, syy=0, txy=0, szz=-25) within `-Tolerance` kPa.

  This is the regression guard for the multi-element T3 dat-typo bug that
  manifested as a 0xC0000409 stack-buffer-overrun in May 2026. Keep it cheap
  enough that CI / pre-commit hooks can run it without thinking.

.PARAMETER ExePath
  Override the solver path (defaults to library/solver/p610_t3_elastoplastic.exe,
  falling back to the Debug build under build/Debug/).

.PARAMETER Tolerance
  Per-component absolute tolerance in kPa. Defaults to 1e-3.

.EXAMPLE
  pwsh -File tools/run_le1_min_repro.ps1
#>
param(
    [string] $ExePath = '',
    [double] $Tolerance = 1.0e-3
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$DatDir   = Join-Path $RepoRoot 'library\solver\test'
$DatStem  = 'le1_min_repro'
$DatFile  = Join-Path $DatDir "$DatStem.dat"
$JsonFile = Join-Path $DatDir "$DatStem.json"

if (-not $ExePath) {
    $candidates = @(
        (Join-Path $RepoRoot 'library\solver\p610_t3_elastoplastic.exe'),
        (Join-Path $RepoRoot 'build\Release\p610_t3_elastoplastic.exe'),
        (Join-Path $RepoRoot 'build\Debug\p610_t3_elastoplastic.exe')
    )
    $ExePath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $ExePath) {
        Write-Error "Could not locate p610_t3_elastoplastic.exe in library/solver or build/{Release,Debug}."
        exit 2
    }
}

if (-not (Test-Path $DatFile)) {
    Write-Error "Reproducer dat not found: $DatFile"
    exit 2
}

if (Test-Path $JsonFile) { Remove-Item $JsonFile -Force }

Write-Host "Solver: $ExePath"
Write-Host "Input : $DatFile"
Push-Location $DatDir
try {
    & $ExePath $DatStem | ForEach-Object { Write-Host "  $_" }
    $rc = $LASTEXITCODE
} finally {
    Pop-Location
}

if ($rc -ne 0) {
    Write-Host "FAIL: solver exit code $rc (expected 0)" -ForegroundColor Red
    exit 1
}
if (-not (Test-Path $JsonFile)) {
    Write-Host "FAIL: solver completed but no JSON output produced" -ForegroundColor Red
    exit 1
}

$rawJson = Get-Content $JsonFile -Raw
$rawJson = [regex]::Replace($rawJson, '(?<![\w.])-?nan(?![\w.])', 'null', 'IgnoreCase')
$rawJson = [regex]::Replace($rawJson, '(?<![\w.])-?inf(?![\w.])', 'null', 'IgnoreCase')
$result  = $rawJson | ConvertFrom-Json

$expected = [pscustomobject]@{ sxx=-100.0; syy=0.0; txy=0.0; szz=-25.0 }
$lastStage = $result.stages[-1]
$failed = $false
foreach ($el in $lastStage.element_stresses) {
    $components = 'sxx','syy','txy','szz'
    foreach ($c in $components) {
        $diff = [math]::Abs([double]$el.$c - [double]$expected.$c)
        if ($diff -gt $Tolerance) {
            Write-Host ("FAIL: element {0} {1}={2:G6} differs from {3:G6} by {4:G6} (>{5:G6})" -f $el.id,$c,$el.$c,$expected.$c,$diff,$Tolerance) -ForegroundColor Red
            $failed = $true
        }
    }
}

if ($failed) {
    exit 1
}

Write-Host ("PASS: 2-element constant-stress field reproduced within {0} kPa" -f $Tolerance) -ForegroundColor Green
exit 0
