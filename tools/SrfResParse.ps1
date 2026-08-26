<#
.SYNOPSIS
  Parse SRF trial rows from p64 .res or SLOPE64 ex4.res.
#>
function Get-SrfResSummary {
    param([string] $ResPath)

    if (-not (Test-Path $ResPath)) { throw "Missing $ResPath" }
    $text = Get-Content $ResPath -Raw
    $limit = 1000
    if ($text -match 'Iteration ceiling=\s*(\d+)') { $limit = [int]$Matches[1] }
    elseif ($text -match '0\.0001\s+(\d+)') { $limit = [int]$Matches[1] }

    $fos = $null
    if ($text -match 'Estimated Factor of Safety\s*=\s*([\d.]+)') {
        $fos = [double]$Matches[1]
    }

    $nosurf = 0
    if ($text -match 'Groundwater:\s*nosurf=(\d+)') { $nosurf = [int]$Matches[1] }
    elseif ($text -match 'Free-surface coordinates') { $nosurf = 22 }

    $trials = [System.Collections.Generic.List[object]]::new()
    $seen = @{}
    foreach ($line in (Get-Content $ResPath)) {
        if ($line -match '^\s*(\d+\.\d+)\s+([\d.eE+-]+)\s+(\d+)\s*$') {
            $srf = [double]$Matches[1]
            $key = "{0:F6}" -f $srf
            if ($seen.ContainsKey($key)) { continue }
            $seen[$key] = $true
            $trials.Add([pscustomobject]@{
                Srf = $srf
                MaxDisp = [double]$Matches[2]
                Iters = [int]$Matches[3]
            }) | Out-Null
        }
    }

    return @{
        Trials = @($trials | Sort-Object Srf)
        Limit = $limit
        FoS = $fos
        Nosurf = $nosurf
    }
}

function Get-FoSFromTrials {
    param($Trials, [int] $Limit)

    $converged = @($Trials | Where-Object { $_.Iters -lt $Limit })
    $failed = @($Trials | Where-Object { $_.Iters -ge $Limit })
    if ($converged.Count -eq 0 -or $failed.Count -eq 0) { return $null }
    $last = $converged | Select-Object -Last 1
    $first = $failed | Select-Object -First 1
    return 0.5 * ($last.Srf + $first.Srf)
}
