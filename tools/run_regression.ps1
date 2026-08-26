<#
.SYNOPSIS
  Run build, GUI, project, and solver regression checks for the FEM app.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/run_regression.ps1

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/run_regression.ps1 -SkipFlaUi -SkipAttachedAutomation
#>
[CmdletBinding()]
param(
    [string] $RepoRoot,
    [switch] $SkipBuild,
    [switch] $SkipFlaUi,
    [switch] $SkipAttachedAutomation,
    [switch] $SkipLoadXfem
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $RepoRoot = Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..')
}
$RepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)

function Invoke-Step {
    param(
        [string] $Name,
        [scriptblock] $Body
    )
    Write-Host ""
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Body
    Write-Host "OK: $Name" -ForegroundColor Green
}

function Invoke-Exe {
    param(
        [string] $FileName,
        [string[]] $Arguments,
        [string] $WorkingDirectory = $RepoRoot
    )
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $FileName
    $psi.Arguments = ($Arguments | ForEach-Object {
        if ($_ -match '\s') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
    }) -join ' '
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    $stdout = $p.StandardOutput.ReadToEnd()
    $stderr = $p.StandardError.ReadToEnd()
    $p.WaitForExit()
    if ($stdout) { Write-Host $stdout.TrimEnd() }
    if ($stderr) { Write-Host $stderr.TrimEnd() -ForegroundColor DarkYellow }
    if ($p.ExitCode -ne 0) {
        throw "$FileName exited with code $($p.ExitCode)"
    }
}

function Invoke-DotNet {
    param([string[]] $Arguments)
    Invoke-Exe -FileName 'dotnet' -Arguments $Arguments
}

function Resolve-RequiredFile {
    param([string] $Path, [string] $Hint)
    if (-not (Test-Path $Path)) {
        throw "$Hint not found: $Path"
    }
    return [System.IO.Path]::GetFullPath($Path)
}

$regressionDir = Join-Path $RepoRoot 'artifacts\regression'
New-Item -ItemType Directory -Force -Path $regressionDir | Out-Null

$guiProject = Join-Path $RepoRoot 'X2D_GUI\GeotechSlopeApp.csproj'
$loadXfemProject = Join-Path $RepoRoot 'X2D_GUI\SmokeTest\LoadXfemSmoke.csproj'
$flaUiProject = Join-Path $RepoRoot 'X2D_GUI\SmokeTest\FlaUiSmoke\FlaUiSmoke.csproj'
$attachedAutomationProject = Join-Path $RepoRoot 'scripts\AttachedSlopeUiAutomation\AttachedSlopeUiAutomation.csproj'
$regressionCheckProject = Join-Path $RepoRoot 'scripts\RegressionCheck\RegressionCheck.csproj'
$t3SrfExe = Join-Path $RepoRoot 'library\solver\SRF\slope_stability_T3_SRF.exe'

if (-not $SkipBuild) {
    Invoke-Step "Build GUI" {
        Invoke-DotNet @('build', $guiProject)
    }
    Invoke-Step "Build regression comparator" {
        Invoke-DotNet @('build', $regressionCheckProject)
    }
    if (-not $SkipFlaUi) {
        Invoke-Step "Build FlaUI smoke test" {
            Invoke-DotNet @('build', $flaUiProject)
        }
    }
}

if (-not $SkipLoadXfem) {
    $attachedXfem = Join-Path $RepoRoot 'Verification\slope\AttachedSlope_T3_SRF\attached_slope_t3_srf.xfem'
    Invoke-Step "XFEM load/save smoke" {
        Resolve-RequiredFile $attachedXfem 'Attached slope XFEM' | Out-Null
        Invoke-DotNet @('run', '--project', $loadXfemProject, '--', $attachedXfem)
    }
}

if (-not $SkipFlaUi) {
    Invoke-Step "FlaUI GUI smoke" {
        Invoke-DotNet @('run', '--project', $flaUiProject)
    }
}

Invoke-Step "SLOPE64 T3 SRF solver regression" {
    $caseDir = Join-Path $regressionDir 'slope64_ex1_t3'
    New-Item -ItemType Directory -Force -Path $caseDir | Out-Null
    $sourceDat = Resolve-RequiredFile (Join-Path $RepoRoot 'scripts\slope_stability_T3_SRF\ex1_homogeneous_slope.dat') 'SLOPE64 T3 deck'
    $caseBase = 'slope64_ex1_t3'
    Copy-Item -Force $sourceDat (Join-Path $caseDir ($caseBase + '.dat'))
    Resolve-RequiredFile $t3SrfExe 'T3 SRF solver' | Out-Null
    Invoke-Exe -FileName $t3SrfExe -Arguments @($caseBase) -WorkingDirectory $caseDir
    Invoke-DotNet @(
        'run', '--project', $regressionCheckProject, '--',
        '--case', 'slope64_ex1_t3',
        '--json', (Join-Path $caseDir ($caseBase + '.json')),
        '--nodes', '825',
        '--elements', '1520',
        '--srf-fos', '1.1',
        '--srf-max-disp', '0.029935051551206806',
        '--srf-max-iters', '2',
        '--rel-tol', '1e-5',
        '--abs-tol', '1e-8'
    )
}

if (-not $SkipAttachedAutomation) {
    Invoke-Step "Attached slope automation" {
        Invoke-DotNet @('run', '--project', $attachedAutomationProject)
    }
}

Invoke-Step "Attached slope T3 SRF regression" {
    $attachedJson = Resolve-RequiredFile (Join-Path $RepoRoot 'Verification\slope\AttachedSlope_T3_SRF\attached_slope_t3_srf_F100.json') 'Attached slope F100 JSON'
    Invoke-DotNet @(
        'run', '--project', $regressionCheckProject, '--',
        '--case', 'attached_slope_t3_srf_F100',
        '--json', $attachedJson,
        '--nodes', '976',
        '--elements', '1800',
        '--srf-fos', '1.0',
        '--srf-max-disp', '0.017016139353337158',
        '--srf-max-iters', '4',
        '--rel-tol', '1e-5',
        '--abs-tol', '1e-8'
    )
}

Write-Host ""
Write-Host "All regression checks passed." -ForegroundColor Green
