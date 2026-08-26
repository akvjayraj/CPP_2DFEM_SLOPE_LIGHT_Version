# Builds a self-contained, locked-license GeotechSlopeApp package under repo\license\
# with all FEM solver dependencies in license\library\solver\.
#
# Usage:
#   .\tools\build_locked_license_package.ps1
#   .\tools\build_locked_license_package.ps1 -Username "user" -Password "secret" -LicenseeName "Acme Corp"
#   .\tools\build_locked_license_package.ps1 -ExpiryDays 30 -SkipRestoreSource

param(
    [string]$Username = "license",
    [string]$Password = "",
    [string]$LicenseeName = "Evaluation User",
    [int]$ExpiryDays = 30,
    [switch]$SkipRestoreSource
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-Sha256Hex([string]$Text) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    $hash = [System.Security.Cryptography.SHA256]::Create().ComputeHash($bytes)
    return ($hash | ForEach-Object { $_.ToString("x2") }) -join ""
}

function Set-LicenseGateConstants {
    param(
        [string]$GatePath,
        [string]$HashHex,
        [string]$Salt,
        [string]$ExpiryIso,
        [string]$Licensee
    )

    $content = Get-Content -LiteralPath $GatePath -Raw
    $content = [regex]::Replace(
        $content,
        '(?m)^(\s*private const string ExpectedCredentialHashHex\s*=\s*")[^"]*(";\s*)$',
        "`${1}$HashHex`${2}")
    $content = [regex]::Replace(
        $content,
        '(?m)^(\s*private const string CredentialSalt\s*=\s*")[^"]*(";\s*)$',
        "`${1}$Salt`${2}")
    $content = [regex]::Replace(
        $content,
        '(?m)^(\s*private const string ExpiryUtcIso\s*=\s*")[^"]*(";\s*)$',
        "`${1}$ExpiryIso`${2}")
    $content = [regex]::Replace(
        $content,
        '(?m)^(\s*public const string LicenseeDisplayName\s*=\s*")[^"]*(";\s*)$',
        "`${1}$Licensee`${2}")

    Set-Content -LiteralPath $GatePath -Value $content -NoNewline -Encoding UTF8
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$guiProject = Join-Path $repoRoot "X2D_GUI\GeotechSlopeApp.csproj"
$gatePath = Join-Path $repoRoot "X2D_GUI\Backend\LicenseGate.cs"
$gateBackup = Join-Path $repoRoot "X2D_GUI\Backend\LicenseGate.cs.buildbak"
$outDir = Join-Path $repoRoot "license"
$solverSrc = Join-Path $repoRoot "library\solver"
$solverOut = Join-Path $outDir "library\solver"
$readmePath = Join-Path $outDir "LICENSE_README.txt"
$credsPath = Join-Path $outDir "LICENSE_CREDENTIALS.txt"

if ([string]::IsNullOrWhiteSpace($Password)) {
    $Password = -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 16 | ForEach-Object { [char]$_ })
}

$expiryUtc = ([DateTime]::UtcNow.Date.AddDays($ExpiryDays)).AddHours(23).AddMinutes(59).AddSeconds(59)
$expiryIso = $expiryUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", [Globalization.CultureInfo]::InvariantCulture)
$salt = [guid]::NewGuid().ToString("N")
$plaintext = "$($Username.Trim()):$Password`:$salt"
$hashHex = Get-Sha256Hex $plaintext

Write-Host "Building locked-license package"
Write-Host "  Licensee : $LicenseeName"
Write-Host "  Username : $Username"
Write-Host "  Expires  : $expiryIso (UTC)"
Write-Host "  Output   : $outDir"

if (-not (Test-Path $guiProject)) { throw "Project not found: $guiProject" }
if (-not (Test-Path $solverSrc)) { throw "Solver folder not found: $solverSrc" }

Copy-Item -LiteralPath $gatePath -Destination $gateBackup -Force
try {
    Set-LicenseGateConstants -GatePath $gatePath -HashHex $hashHex -Salt $salt -ExpiryIso $expiryIso -Licensee $LicenseeName

    if (Test-Path $outDir) {
        Remove-Item -LiteralPath $outDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null

    Push-Location (Join-Path $repoRoot "X2D_GUI")
    try {
        dotnet publish GeotechSlopeApp.csproj `
            -c Release `
            -r win-x64 `
            --self-contained true `
            -p:LockedLicense=true `
            -p:PublishSingleFile=false `
            -o $outDir
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
    }
    finally {
        Pop-Location
    }

    New-Item -ItemType Directory -Path $solverOut -Force | Out-Null
    Get-ChildItem -LiteralPath $solverSrc -Filter "*.exe" | Copy-Item -Destination $solverOut -Force

    $meshDllCandidates = @(
        (Join-Path $repoRoot "build\Release\mesh2d_native.dll"),
        (Join-Path $repoRoot "build\Debug\mesh2d_native.dll"),
        (Join-Path $outDir "mesh2d_native.dll")
    )
    foreach ($candidate in $meshDllCandidates) {
        if (Test-Path $candidate) {
            Copy-Item -LiteralPath $candidate -Destination $solverOut -Force
            break
        }
    }

    $solverFiles = Get-ChildItem -LiteralPath $solverOut -File | Sort-Object Name
    $solverList = ($solverFiles | ForEach-Object { "  - $($_.Name)" }) -join [Environment]::NewLine

    @"
GeotechSlopeApp - Locked License Package
========================================

Built: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss") (local time)
Licensee: $LicenseeName
Valid until: $expiryIso (UTC)

Run the application
-------------------
1. Open this folder in Explorer.
2. Double-click GeotechSlopeApp.exe.
3. Sign in when prompted (see LICENSE_CREDENTIALS.txt).

Layout
------
GeotechSlopeApp.exe          Main application (self-contained .NET 8, win-x64)
library\solver\              FEM executables used by FE Analysis and Script Editor

Solver binaries included
------------------------
$solverList

Notes
-----
- Do not move solver executables out of library\solver; the app resolves them relative to GeotechSlopeApp.exe.
- The license checks UTC expiry and resists casual system-clock rollback via HKCU\Software\GeotechSlopeApp\License.
- After expiry, request a renewed build from the application owner.
"@ | Set-Content -LiteralPath $readmePath -Encoding UTF8

    @"
CONFIDENTIAL - share only with the licensed user.

Username : $Username
Password : $Password
Expires  : $expiryIso (UTC)
Licensee : $LicenseeName
"@ | Set-Content -LiteralPath $credsPath -Encoding UTF8

    Write-Host ""
    Write-Host "Package ready: $outDir"
    Write-Host "Credentials written to: $credsPath"
}
finally {
    if (-not $SkipRestoreSource) {
        if (Test-Path $gateBackup) {
            Move-Item -LiteralPath $gateBackup -Destination $gatePath -Force
            Write-Host "Restored source LicenseGate.cs (dev builds remain unlocked)."
        }
    }
    elseif (Test-Path $gateBackup) {
        Write-Host "Left LicenseGate.cs patched (-SkipRestoreSource)."
    }
}
