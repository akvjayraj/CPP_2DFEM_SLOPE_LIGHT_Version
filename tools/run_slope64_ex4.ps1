<#
.SYNOPSIS
  Run bundled SLOPE64 slope64.exe on ex4 (stdin base name).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/run_slope64_ex4.ps1
  powershell -ExecutionPolicy Bypass -File tools/run_slope64_ex4.ps1 -Fetch
#>
[CmdletBinding()]
param(
    [string] $RepoRoot,
    [switch] $Fetch,
    [string] $BaseName = 'ex4'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $RepoRoot = Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..')
}

$refDir = Join-Path $RepoRoot 'benchmarks\slope64_ref'
$exe = Join-Path $refDir 'slope64.exe'
$dat = Join-Path $refDir "$BaseName.dat"

if ($Fetch -or -not (Test-Path $dat)) {
    & powershell -ExecutionPolicy Bypass -File (Join-Path $RepoRoot 'tools\fetch_slope64_ex4.ps1') -RepoRoot $RepoRoot
}

if (-not (Test-Path $exe)) {
    throw "Missing $exe - copy SLOPE64 executable into benchmarks/slope64_ref/"
}
if (-not (Test-Path $dat)) {
    throw "Missing $dat"
}

Write-Host "Running slope64.exe on $BaseName ..."
Push-Location $refDir
try {
    $BaseName | & $exe
    if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) {
        throw "slope64.exe exited with code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

$res = Join-Path $refDir "$BaseName.res"
if (-not (Test-Path $res)) {
    throw "Expected output not found: $res"
}

Write-Host "Results: $res"
Get-Content $res -Tail 20
