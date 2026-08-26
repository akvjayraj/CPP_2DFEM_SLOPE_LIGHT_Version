<#
.SYNOPSIS
  Run multi-trial SRF verification via fem_cpp p64 (explicit T3/T6/Q4/Q8 mesh).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/run_new6_ex2_srf_verify.ps1
  powershell -ExecutionPolicy Bypass -File tools/run_new6_ex2_srf_verify.ps1 -SolverExe path\to\fem_cpp.exe -DatPath path\to\p64_explicit.dat
#>
[CmdletBinding()]
param(
    [string] $RepoRoot,
    [string] $DatPath,
    [string] $SolverExe,
    [string] $WorkDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $RepoRoot = Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..')
}
if (-not $DatPath) {
    $DatPath = Join-Path $RepoRoot 'Verification\slope\Ex2_twolayerslope\new_6_ex2_solver.dat'
}
if (-not $SolverExe) {
    $candidates = @(
        (Join-Path $RepoRoot 'build\fem_cpp.exe'),
        (Join-Path $RepoRoot 'library\solver\fem_cpp.exe'),
        (Join-Path $RepoRoot 'library\solver\slope_stability_T3_SRF.exe')
    )
    $SolverExe = ($candidates | Where-Object { Test-Path $_ } | Select-Object -First 1)
    if (-not $SolverExe) { $SolverExe = $candidates[0] }
}
if (-not $WorkDir) {
    $WorkDir = Join-Path $RepoRoot 'Verification\slope\Ex2_twolayerslope\_srf_run'
}

if (-not (Test-Path $DatPath)) {
    throw "Patched dat not found: $DatPath (run tools/patch_new6_ex2_solver.ps1 first)"
}
if (-not (Test-Path $SolverExe)) {
    throw "Solver not found: $SolverExe (build fem_cpp first)"
}

New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null

function Patch-SrfF([string]$Content, [double]$F) {
    if ($Content -match '(?m)^\s*!\s*srf_F\s+[0-9\.Ee\+\-]+') {
        return [regex]::Replace($Content, '(?m)^(\s*!\s*srf_F\s+)[0-9\.Ee\+\-]+', "`${1}$F", 1)
    }
    return $Content -replace '(! <END:AnalysisOptions>)', "! srf_F $F`r`n`$1"
}

function Ensure-P64ExplicitDeck([string]$Content) {
    if ($Content -notmatch '(?m)^\s*!\s*<solver:p64>') {
        $Content = "! <solver:p64>`r`n" + $Content
    }
    $Content = [regex]::Replace($Content, '(?m)^\s*!\s*solver_executable\s+\S+\s*$', '! solver_executable SRF')
    if ($Content -notmatch '(?m)^\s*!\s*mesh_source\s+explicit') {
        $Content = [regex]::Replace($Content, '(! <END:AnalysisOptions>)', "! mesh_source explicit`r`n`$1", 1)
    }
    return $Content
}

$useFemCpp = ($SolverExe -match 'fem_cpp')
$baseDat = Get-Content -Raw -Path $DatPath
if ($useFemCpp) {
    $baseDat = Ensure-P64ExplicitDeck $baseDat
} else {
    $baseDat = [regex]::Replace($baseDat, '(?m)^\s*!\s*solver_executable\s+\S+\s*$', '! solver_executable slope_stability_T3_SRF')
}$trials = @(1.0, 1.1, 1.2, 1.3, 1.4, 1.5)
$rows = New-Object System.Collections.Generic.List[object]

Write-Host "Running $($trials.Count) SRF trials in $WorkDir"
foreach ($f in $trials) {
    $trialName = "new_6_ex2_srf_{0}" -f ($f.ToString('0.0').Replace('.', '_'))
    $trialDat = Patch-SrfF $baseDat $f
    $datFile = Join-Path $WorkDir ($trialName + '.dat')
    Set-Content -Path $datFile -Value $trialDat -NoNewline

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $SolverExe
    $psi.Arguments = $trialName    $psi.WorkingDirectory = $WorkDir
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    $stdout = $p.StandardOutput.ReadToEnd()
    $stderr = $p.StandardError.ReadToEnd()
    $p.WaitForExit()

    $jsonPath = Join-Path $WorkDir ($trialName + '.json')
    $resPath = Join-Path $WorkDir ($trialName + '.res')
    $umax = [double]::NaN
    $yield = [double]::NaN
    $stages = 0
    $converged = $false
    if (Test-Path $jsonPath) {
        $j = Get-Content -Raw $jsonPath | ConvertFrom-Json
        if ($null -ne $j.n_stages) { $stages = [int]$j.n_stages }
        if ($j.meta -and $j.meta.srf) {
            $umax = [double]$j.meta.srf.max_disp
            $yield = [double]$j.meta.srf.max_yield_ratio
            $converged = [bool]$j.meta.srf.all_increments_converged
        }
        elseif ($j.nodes) {
            $umax = ($j.nodes | ForEach-Object { [Math]::Sqrt($_.ux * $_.ux + $_.uy * $_.uy) } | Measure-Object -Maximum).Maximum
        }
    }

    $rows.Add([pscustomobject]@{
        F = $f
        ExitCode = $p.ExitCode
        Stages = $stages
        Umax = $umax
        MaxYield = $yield
        Converged = $converged
        HasJson = (Test-Path $jsonPath)
        HasRes = (Test-Path $resPath)
        LogTail = (($stdout + $stderr).Split([Environment]::NewLine) | Where-Object { $_.Length -gt 0 } | Select-Object -Last 3) -join '; '
    }) | Out-Null

    Write-Host ("F={0:G4} exit={1} stages={2} umax={3:G6} yield={4:G4} conv={5}" -f $f, $p.ExitCode, $stages, $umax, $yield, $converged)
}

Write-Host ''
Write-Host 'Trial summary (expect umax to grow and/or exit code to change near FoS~1.5):'
$rows | Format-Table -AutoSize
