<#
.SYNOPSIS
  Add structural block comments to Verification/*.dat files.

.DESCRIPTION
  The verification suite contains several .dat dialects. This annotator keeps
  every solver data token unchanged and inserts full-line comments above
  logical structural blocks only. Generated comments are removed and regenerated
  on every run so the tool is idempotent.

  Existing hand-written comments are preserved.
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string] $RepoRoot = (Resolve-Path '.').Path,
    [string] $VerificationRoot,
    [switch] $Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $VerificationRoot) {
    $VerificationRoot = Join-Path $RepoRoot 'Verification'
}
if (-not (Test-Path $VerificationRoot)) {
    throw "Verification folder not found: $VerificationRoot"
}

$OldLinePrefix = '! dat-format:'
$GeneratedBlockPrefixes = @(
    '! Solver metadata block:',
    '! Mesh size block:',
    '! Material/property block:',
    '! Nodal coordinate block:',
    '! Element connectivity block:',
    '! Boundary condition block:',
    '! Output/watch node block:',
    '! Solver control block:',
    '! Traction loading block:',
    '! Additional solver data block:',
    '! P51 analysis type block:',
    '! P51 element definition block:',
    '! P51 grid/material-count block:',
    '! P51 material/property block:',
    '! P51 element material-type block:',
    '! P51 x-coordinate block:',
    '! P51 y-coordinate block:',
    '! P51 boundary condition block:',
    '! P51 nodal load block:',
    '! P51 fixed freedom block:',
    '! Legacy/generic data block:'
)
$GeneratedBlockContinuationPrefixes = @(
    '! This block',
    '! nels',
    '! nn =',
    '! np_types',
    '! One row',
    '! Each row',
    '! Each following',
    '! First line',
    '! Following row',
    '! For T3',
    '! For T6',
    '! For Q4',
    '! For Q8',
    '! The node count',
    '! 0 =',
    '! tol ',
    '! limit ',
    '! incs ',
    '! layers ',
    '! Each group',
    '! mode row:',
    '! edge count:',
    '! edge rows:',
    '! type_2d',
    '! element nod dir',
    '! element =',
    '! nod =',
    '! dir =',
    '! nxe nye nip np_types',
    '! nxe =',
    '! nye =',
    '! nip =',
    '! etype',
    '! x_coords',
    '! y_coords',
    '! fixed_freedoms',
    '! loaded_nodes',
    '! phi',
    '! Preserves',
    '! Values are'
)
$GeneratedStandaloneLines = @(
    "! 'plane' = plane strain; 'axisymmetric' = radial/z axisymmetric strain.",
    '! 1 = first material row, 2 = second material row, and so on.'
)

function Test-CommentLine {
    param([string] $Line)
    return $Line.TrimStart().StartsWith('!')
}

function Test-BlankLine {
    param([string] $Line)
    return [string]::IsNullOrWhiteSpace($Line)
}

function Remove-GeneratedComments {
    param([string[]] $Lines)
    $kept = New-Object System.Collections.Generic.List[string]
    $removingBlock = $false
    foreach ($line in $Lines) {
        $trimmed = $line.TrimStart()
        if ($trimmed.StartsWith($OldLinePrefix)) {
            $removingBlock = $false
            continue
        }

        if ($GeneratedStandaloneLines -contains $trimmed) {
            $removingBlock = $false
            continue
        }

        $startsBlock = $false
        foreach ($prefix in $GeneratedBlockPrefixes) {
            if ($trimmed.StartsWith($prefix)) {
                $startsBlock = $true
                break
            }
        }
        if ($startsBlock) {
            $removingBlock = $true
            continue
        }

        if ($removingBlock) {
            $isGeneratedContinuation = $false
            foreach ($prefix in $GeneratedBlockContinuationPrefixes) {
                if ($trimmed.StartsWith($prefix)) {
                    $isGeneratedContinuation = $true
                    break
                }
            }
            if ($isGeneratedContinuation) {
                continue
            }
            $removingBlock = $false
        }

        $kept.Add($line) | Out-Null
    }
    return $kept.ToArray()
}

function Split-Tokens {
    param([string] $Line)
    return @($Line.Trim() -split '\s+' | Where-Object { $_ -ne '' })
}

function Test-IntegerToken {
    param([string] $Token)
    return $Token -match '^[+-]?\d+$'
}

function Test-NumberToken {
    param([string] $Token)
    return $Token -match '^[+-]?((\d+\.?\d*)|(\.\d+))([eEdD][+-]?\d+)?$'
}

function Get-DataRows {
    param([string[]] $Lines)

    $rows = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $Lines.Count; $i++) {
        $line = $Lines[$i]
        if (Test-BlankLine $line) { continue }
        if (Test-CommentLine $line) { continue }
        $rows.Add([pscustomobject]@{
            SourceIndex = $i
            Text = $line
            Tokens = @(Split-Tokens $line)
        }) | Out-Null
    }
    return $rows.ToArray()
}

function Get-Dialect {
    param([object[]] $Rows, [string[]] $Lines)

    if ($Rows.Count -ge 3) {
        $first = $Rows[0].Tokens
        $second = $Rows[1].Tokens
        $third = $Rows[2].Tokens
        if ($first.Count -ge 1 -and $first[0].Trim("'`"").ToLowerInvariant() -in @('plane','axisymmetric')) {
            return 'p51'
        }
        if ($second.Count -ge 3 -and $second[0].Trim("'`"").ToLowerInvariant() -in @('triangle','quadrilateral')) {
            return 'p51'
        }
        if ($third.Count -eq 4 -and ($third | Where-Object { Test-IntegerToken $_ }).Count -eq 4) {
            $maybeP51 = $Lines | Select-String -Pattern "'triangle'|'quadrilateral'|type_2d" -SimpleMatch -Quiet
            if ($maybeP51) { return 'p51' }
        }
    }

    if ($Rows.Count -ge 1) {
        $tokens = $Rows[0].Tokens
        if ($tokens.Count -eq 3 -and ($tokens | Where-Object { Test-IntegerToken $_ }).Count -eq 3) {
            return 'p610'
        }
    }

    return 'generic'
}

function Get-ElementName {
    param([int] $NodeCount)
    switch ($NodeCount) {
        3 { return 'T3 triangle' }
        4 { return 'Q4 quadrilateral' }
        6 { return 'T6 quadratic triangle' }
        8 { return 'Q8 quadratic quadrilateral' }
        default { return "$NodeCount-node element" }
    }
}

function Add-Block {
    param(
        [hashtable] $Blocks,
        [int] $RowIndex,
        [string[]] $Lines
    )
    if ($RowIndex -lt 0) { return }
    if (-not $Blocks.ContainsKey($RowIndex)) {
        $Blocks[$RowIndex] = New-Object System.Collections.Generic.List[string]
    }
    foreach ($line in $Lines) {
        $Blocks[$RowIndex].Add($line) | Out-Null
    }
}

function Get-P610Blocks {
    param([object[]] $Rows)

    $blocks = @{}
    if ($Rows.Count -eq 0) { return $blocks }

    $header = $Rows[0].Tokens
    if ($header.Count -ne 3) { return $blocks }
    $nels = [int]$header[0]
    $nn = [int]$header[1]
    $npTypes = [int]$header[2]
    Add-Block $blocks 0 @(
        '! Mesh size block:',
        '! nels nn np_types',
        '! nels = number of elements',
        '! nn = number of nodes',
        '! np_types = number of material/property types'
    )

    $pos = 1
    if ($pos -lt $Rows.Count) {
        Add-Block $blocks $pos @(
            '! Material/property block:',
            '! phi   c     psi  gamma  E        nu    K0',
            '! One row per material/property type declared by np_types.',
            '! Values are consumed by the selected solver in solver-specific order.'
        )
    }
    for ($m = 1; $m -le $npTypes -and $pos -lt $Rows.Count; $m++, $pos++) {
    }

    if ($pos -lt $Rows.Count) {
        Add-Block $blocks $pos @(
            '! Nodal coordinate block:',
            '! One row per node in node-number order.',
            '! Each row is: x y'
        )
    }
    for ($n = 1; $n -le $nn -and $pos -lt $Rows.Count; $n++, $pos++) {
    }

    if ($pos -lt $Rows.Count) {
        $firstConnectivityWidth = $Rows[$pos].Tokens.Count
        $elementName = Get-ElementName $firstConnectivityWidth
        Add-Block $blocks $pos @(
            '! Element connectivity block:',
            '! One row per element in element-number order.',
            "! The node count per row identifies the element type; first row appears to be $elementName."
        )
    }
    for ($e = 1; $e -le $nels -and $pos -lt $Rows.Count; $e++, $pos++) {
    }

    if ($pos -ge $Rows.Count) { return $blocks }
    $nr = 0
    if ($Rows[$pos].Tokens.Count -gt 0 -and (Test-IntegerToken $Rows[$pos].Tokens[0])) {
        $nr = [int]$Rows[$pos].Tokens[0]
    }
    Add-Block $blocks $pos @(
        '! Boundary condition block:',
        '! First line is nr = number of restrained nodes.',
        '! Each following row is: node_id fix_x fix_y',
        '! 0 = fixed/restrained, 1 = free.'
    )
    $pos++

    for ($i = 1; $i -le $nr -and $pos -lt $Rows.Count; $i++, $pos++) {
    }

    if ($pos -ge $Rows.Count) { return $blocks }
    $nouts = 0
    if ($Rows[$pos].Tokens.Count -gt 0 -and (Test-IntegerToken $Rows[$pos].Tokens[0])) {
        $nouts = [int]$Rows[$pos].Tokens[0]
    }
    Add-Block $blocks $pos @(
        '! Output/watch node block:',
        '! First line is nouts = number of nodes requested for output.',
        '! Following row(s) list the node IDs.'
    )
    $pos++

    $seenOut = 0
    while ($pos -lt $Rows.Count -and $seenOut -lt $nouts) {
        $countThis = @($Rows[$pos].Tokens | Where-Object { Test-IntegerToken $_ }).Count
        $seenOut += $countThis
        $pos++
    }

    if ($pos -lt $Rows.Count) {
        $tokens = $Rows[$pos].Tokens
        $looksLikeControls = $tokens.Count -eq 4 -and (Test-NumberToken $tokens[0]) -and (Test-NumberToken $tokens[1]) -and (Test-IntegerToken $tokens[2]) -and (Test-IntegerToken $tokens[3])
        if ($looksLikeControls) {
            Add-Block $blocks $pos @(
                '! Solver control block:',
                '! tol limit incs layers',
                '! tol = convergence tolerance',
                '! limit = maximum iterations',
                '! incs = load increments',
                '! layers = solver path/layer selector'
            )
            $pos++
        }
    }

    if ($pos -ge $Rows.Count) { return $blocks }
    $ngroups = 0
    if ($Rows[$pos].Tokens.Count -gt 0 -and (Test-IntegerToken $Rows[$pos].Tokens[0])) {
        $ngroups = [int]$Rows[$pos].Tokens[0]
    }
    Add-Block $blocks $pos @(
        '! Traction loading block:',
        '! First line is number of traction groups.',
        '! Each group then contains:',
        '! mode row: mode and stress/traction parameters',
        '! edge count: number of loaded edges',
        '! edge rows: node IDs defining each loaded boundary edge'
    )
    $pos++

    for ($g = 1; $g -le $ngroups -and $pos -lt $Rows.Count; $g++) {
        $pos++
        if ($pos -ge $Rows.Count) { break }
        $edgeCount = 0
        if ($Rows[$pos].Tokens.Count -gt 0 -and (Test-IntegerToken $Rows[$pos].Tokens[0])) {
            $edgeCount = [int]$Rows[$pos].Tokens[0]
        }
        $pos++
        for ($edge = 1; $edge -le $edgeCount -and $pos -lt $Rows.Count; $edge++, $pos++) {
        }
    }

    if ($pos -lt $Rows.Count) {
        Add-Block $blocks $pos @(
            '! Additional solver data block:',
            '! Preserves legacy solver rows in their original order.'
        )
    }

    return $blocks
}

function Get-P51Blocks {
    param([object[]] $Rows)

    $blocks = @{}
    if ($Rows.Count -eq 0) { return $blocks }
    $pos = 0

    if ($Rows[$pos].Tokens[0] -eq 'type_2d') {
        Add-Block $blocks $pos @(
            '! P51 analysis type block:',
            '! type_2d keyword followed by plane or axisymmetric analysis type.'
        )
        $pos++
    }
    if ($pos -lt $Rows.Count) {
        if (-not $blocks.ContainsKey($pos)) {
            Add-Block $blocks $pos @(
                '! P51 analysis type block:',
                "! 'plane' = plane strain; 'axisymmetric' = radial/z axisymmetric strain."
            )
        }
        $pos++
    }
    if ($pos -lt $Rows.Count) {
        Add-Block $blocks $pos @(
            '! P51 element definition block:',
            '! element nod dir',
            '! element = triangle or quadrilateral family',
            '! nod = nodes per element',
            '! dir = mesh numbering direction'
        )
        $pos++
    }
    $nxe = 0; $nye = 0; $npTypes = 1
    if ($pos -lt $Rows.Count -and $Rows[$pos].Tokens.Count -ge 4) {
        $nxe = [int]$Rows[$pos].Tokens[0]
        $nye = [int]$Rows[$pos].Tokens[1]
        $npTypes = [int]$Rows[$pos].Tokens[3]
        Add-Block $blocks $pos @(
            '! P51 grid/material-count block:',
            '! nxe nye nip np_types',
            '! nxe = number of x-direction element divisions',
            '! nye = number of y-direction element divisions',
            '! nip = number of integration points',
            '! np_types = number of material/property types'
        )
        $pos++
    }

    if ($pos -lt $Rows.Count) {
        Add-Block $blocks $pos @(
            '! P51 material/property block:',
            "! One row per material type: Young's modulus E and Poisson ratio nu."
        )
    }
    for ($m = 1; $m -le $npTypes -and $pos -lt $Rows.Count; $m++, $pos++) {
    }

    if ($npTypes -gt 1) {
        $nels = $nxe * $nye * 2
        $seen = 0
        if ($pos -lt $Rows.Count) {
            Add-Block $blocks $pos @(
                '! P51 element material-type block:',
                '! etype values assign one material ID to each element.',
                '! 1 = first material row, 2 = second material row, and so on.'
            )
        }
        while ($pos -lt $Rows.Count -and $seen -lt $nels) {
            $seen += $Rows[$pos].Tokens.Count
            $pos++
        }
    }

    if ($pos -lt $Rows.Count) {
        Add-Block $blocks $pos @(
            '! P51 x-coordinate block:',
            '! x_coords = grid x coordinates in model units.'
        )
        $pos++
    }
    if ($pos -lt $Rows.Count) {
        Add-Block $blocks $pos @(
            '! P51 y-coordinate block:',
            '! y_coords = grid y coordinates in model units.'
        )
        $pos++
    }
    if ($pos -lt $Rows.Count) {
        $nr = [int]$Rows[$pos].Tokens[0]
        Add-Block $blocks $pos @(
            '! P51 boundary condition block:',
            '! First line is nr = number of restrained nodes.',
            '! Each following row is: node_id nf_x nf_y',
            '! 0 = fixed/restrained, 1 = free.'
        )
        $pos++
        for ($i = 1; $i -le $nr -and $pos -lt $Rows.Count; $i++, $pos++) {
        }
    }
    if ($pos -lt $Rows.Count) {
        $loaded = [int]$Rows[$pos].Tokens[0]
        Add-Block $blocks $pos @(
            '! P51 nodal load block:',
            '! First line is loaded_nodes = number of nodal load rows.',
            '! Each following row is: node_id load_x load_y.'
        )
        $pos++
        for ($i = 1; $i -le $loaded -and $pos -lt $Rows.Count; $i++, $pos++) {
        }
    }
    if ($pos -lt $Rows.Count) {
        $fixed = [int]$Rows[$pos].Tokens[0]
        Add-Block $blocks $pos @(
            '! P51 fixed freedom block:',
            '! fixed_freedoms = number of prescribed displacement rows.',
            '! Each following row is: node_id sense value, where sense 1=x and 2=y.'
        )
        $pos++
        for ($i = 1; $i -le $fixed -and $pos -lt $Rows.Count; $i++, $pos++) {
        }
    }
    if ($pos -lt $Rows.Count) {
        Add-Block $blocks $pos @(
            '! Additional solver data block:',
            '! Preserves legacy p51 rows in their original order.'
        )
    }
    return $blocks
}

function Get-GenericBlocks {
    param([object[]] $Rows)
    $blocks = @{}
    if ($Rows.Count -gt 0) {
        Add-Block $blocks 0 @(
            '! Legacy/generic data block:',
            '! This block preserves solver input rows in their original order.'
        )
    }
    return $blocks
}

function Annotate-DatFile {
    param([string] $Path)

    $original = [System.IO.File]::ReadAllLines($Path)
    $clean = Remove-GeneratedComments $original
    $rows = @(Get-DataRows $clean)
    $dialect = Get-Dialect -Rows $rows -Lines $clean
    $blocks = switch ($dialect) {
        'p610' { Get-P610Blocks -Rows $rows }
        'p51' { Get-P51Blocks -Rows $rows }
        default { Get-GenericBlocks -Rows $rows }
    }

    $rowBySourceIndex = @{}
    for ($i = 0; $i -lt $rows.Count; $i++) {
        $rowBySourceIndex[[int]$rows[$i].SourceIndex] = $i
    }

    $out = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $clean.Count; $i++) {
        if ($rowBySourceIndex.ContainsKey($i)) {
            $rowIndex = [int]$rowBySourceIndex[$i]
            if ($blocks.ContainsKey($rowIndex)) {
                foreach ($commentLine in $blocks[$rowIndex]) {
                    $out.Add($commentLine) | Out-Null
                }
            }
        }
        $out.Add($clean[$i]) | Out-Null
    }

    $newText = ($out -join [Environment]::NewLine) + [Environment]::NewLine
    $oldText = ([string]::Join([Environment]::NewLine, $original)) + [Environment]::NewLine
    $changed = $newText -ne $oldText
    return [pscustomobject]@{
        Path = $Path
        Dialect = $dialect
        DataRows = $rows.Count
        Changed = $changed
        Text = $newText
    }
}

$files = @(Get-ChildItem -Path $VerificationRoot -Recurse -Filter '*.dat' -File | Sort-Object FullName)
$results = New-Object System.Collections.Generic.List[object]
$changedCount = 0

foreach ($file in $files) {
    $result = Annotate-DatFile -Path $file.FullName
    $results.Add($result) | Out-Null
    if ($result.Changed) {
        $changedCount++
        Write-Verbose ("would update: {0}" -f $file.FullName)
        if (-not $Check) {
            if ($PSCmdlet.ShouldProcess($file.FullName, 'annotate dat-format comments')) {
                [System.IO.File]::WriteAllText($file.FullName, $result.Text, [System.Text.Encoding]::ASCII)
            }
        }
    }
}

$summary = $results | Group-Object Dialect | Sort-Object Name | ForEach-Object {
    "{0}: {1}" -f $_.Name, $_.Count
}
Write-Host ("DAT files scanned: {0}" -f $files.Count)
Write-Host ("Changed files: {0}" -f $changedCount)
Write-Host ("Dialects: {0}" -f ($summary -join ', '))

if ($Check -and $changedCount -gt 0) {
    throw "$changedCount .dat file(s) need dat-format annotations. Run without -Check to update them."
}
