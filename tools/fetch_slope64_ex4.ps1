<#
.SYNOPSIS
  Download SLOPE64 Example 4 (ex4.dat) from Mines into benchmarks/slope64_ref.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/fetch_slope64_ex4.ps1
#>
[CmdletBinding()]
param(
    [string] $RepoRoot,
    [string] $Url = 'https://inside.mines.edu/~vgriffit/slope64/ex4.dat'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $RepoRoot = Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..')
}

$refDir = Join-Path $RepoRoot 'benchmarks\slope64_ref'
$outPath = Join-Path $refDir 'ex4.dat'
if (-not (Test-Path $refDir)) {
    New-Item -ItemType Directory -Path $refDir -Force | Out-Null
}

Write-Host "Downloading $Url ..."
Invoke-WebRequest -Uri $Url -OutFile $outPath -UseBasicParsing
Write-Host "Wrote $outPath ($((Get-Item $outPath).Length) bytes)"
