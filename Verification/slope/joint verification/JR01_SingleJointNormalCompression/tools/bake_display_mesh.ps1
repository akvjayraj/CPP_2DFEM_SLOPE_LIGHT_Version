$ErrorActionPreference = "Stop"
$case = Split-Path -Parent $PSScriptRoot
$path = Join-Path $case "jr01_single_joint_normal_compression.xfem"
$text = [System.IO.File]::ReadAllText($path)

$nx = 8
$nyL = 8
$nyU = 8
$nxl = $nx + 1
$nLower = $nxl * ($nyL + 1)

function NodeL([int]$ix, [int]$iy) { return $iy * $nxl + $ix + 1 }
function NodeU([int]$ix, [int]$iy) { return $nLower + $iy * $nxl + $ix + 1 }

$xs = New-Object 'double[]' $nxl
$ysL = New-Object 'double[]' ($nyL + 1)
$ysU = New-Object 'double[]' ($nyU + 1)
for ($ix = 0; $ix -lt $nxl; $ix++) { $xs[$ix] = $ix / [double]$nx }
for ($iy = 0; $iy -le $nyL; $iy++) { $ysL[$iy] = $iy / [double]$nyL }
for ($iy = 0; $iy -le $nyU; $iy++) { $ysU[$iy] = 1.0 + $iy / [double]$nyU }

$nodeX = New-Object 'double[]' ($nLower * 2)
$nodeY = New-Object 'double[]' ($nLower * 2)
$ni = 0
for ($iy = 0; $iy -le $nyL; $iy++) {
    for ($ix = 0; $ix -lt $nxl; $ix++) {
        $nodeX[$ni] = $xs[$ix]
        $nodeY[$ni] = $ysL[$iy]
        $ni++
    }
}
for ($iy = 0; $iy -le $nyU; $iy++) {
    for ($ix = 0; $ix -lt $nxl; $ix++) {
        $nodeX[$ni] = $xs[$ix]
        $nodeY[$ni] = $ysU[$iy]
        $ni++
    }
}
$nn = $ni

function F([double]$v) {
    return $v.ToString("0.########", [System.Globalization.CultureInfo]::InvariantCulture)
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("    `"discretizationNodes`": [")
for ($i = 0; $i -lt $nn; $i++) {
    $comma = if ($i -lt $nn - 1) { "," } else { "" }
    [void]$sb.AppendLine("      { `"x`": $(F $nodeX[$i]), `"y`": $(F $nodeY[$i]) }$comma")
}
[void]$sb.AppendLine("    ],")

$tris = New-Object System.Collections.Generic.List[object]
function Add-Tri([int]$n1, [int]$n2, [int]$n3) {
    $tris.Add([pscustomobject]@{ n1 = $n1; n2 = $n2; n3 = $n3 })
}
function Add-QuadPair([int]$n00, [int]$n10, [int]$n11, [int]$n01) {
    Add-Tri $n00 $n10 $n11
    Add-Tri $n00 $n11 $n01
}
for ($iy = 0; $iy -lt $nyL; $iy++) {
    for ($ix = 0; $ix -lt $nx; $ix++) {
        Add-QuadPair (NodeL $ix $iy) (NodeL ($ix + 1) $iy) (NodeL ($ix + 1) ($iy + 1)) (NodeL $ix ($iy + 1))
    }
}
for ($iy = 0; $iy -lt $nyU; $iy++) {
    for ($ix = 0; $ix -lt $nx; $ix++) {
        Add-QuadPair (NodeU $ix $iy) (NodeU ($ix + 1) $iy) (NodeU ($ix + 1) ($iy + 1)) (NodeU $ix ($iy + 1))
    }
}

[void]$sb.AppendLine("    `"elements`": [")
for ($e = 0; $e -lt $tris.Count; $e++) {
    $t = $tris[$e]
    $p1x = $nodeX[$t.n1 - 1]; $p1y = $nodeY[$t.n1 - 1]
    $p2x = $nodeX[$t.n2 - 1]; $p2y = $nodeY[$t.n2 - 1]
    $p3x = $nodeX[$t.n3 - 1]; $p3y = $nodeY[$t.n3 - 1]
    $cx = ($p1x + $p2x + $p3x) / 3.0
    $cy = ($p1y + $p2y + $p3y) / 3.0
    $comma = if ($e -lt $tris.Count - 1) { "," } else { "" }
    [void]$sb.AppendLine("      {")
    [void]$sb.AppendLine("        `"displayId`": $($e + 1),")
    [void]$sb.AppendLine("        `"isActive`": true,")
    [void]$sb.AppendLine("        `"isHighlighted`": false,")
    [void]$sb.AppendLine("        `"isQuadraticTriangle`": false,")
    [void]$sb.AppendLine("        `"isQuadrilateral`": false,")
    [void]$sb.AppendLine("        `"isQuadraticQuad`": false,")
    [void]$sb.AppendLine("        `"isUnmatchedTriangle`": false,")
    [void]$sb.AppendLine("        `"centerX`": $(F $cx),")
    [void]$sb.AppendLine("        `"centerY`": $(F $cy),")
    [void]$sb.AppendLine("        `"nodePoints`": [")
    [void]$sb.AppendLine("          { `"x`": $(F $p1x), `"y`": $(F $p1y) },")
    [void]$sb.AppendLine("          { `"x`": $(F $p2x), `"y`": $(F $p2y) },")
    [void]$sb.AppendLine("          { `"x`": $(F $p3x), `"y`": $(F $p3y) }")
    [void]$sb.AppendLine("        ]")
    [void]$sb.AppendLine("      }$comma")
}
[void]$sb.AppendLine("    ],")

[void]$sb.AppendLine("    `"informationRows`": [")
for ($e = 0; $e -lt $tris.Count; $e++) {
    $t = $tris[$e]
    $p1x = $nodeX[$t.n1 - 1]; $p1y = $nodeY[$t.n1 - 1]
    $p2x = $nodeX[$t.n2 - 1]; $p2y = $nodeY[$t.n2 - 1]
    $p3x = $nodeX[$t.n3 - 1]; $p3y = $nodeY[$t.n3 - 1]
    $comma = if ($e -lt $tris.Count - 1) { "," } else { "" }
    [void]$sb.AppendLine("      {")
    [void]$sb.AppendLine("        `"elementId`": $($e + 1),")
    [void]$sb.AppendLine("        `"node1`": $($t.n1), `"node2`": $($t.n2), `"node3`": $($t.n3),")
    [void]$sb.AppendLine("        `"node4`": 0, `"node5`": 0, `"node6`": 0, `"node7`": 0, `"node8`": 0,")
    [void]$sb.AppendLine("        `"etype`": 1,")
    [void]$sb.AppendLine("        `"n1X`": $(F $p1x), `"n1Y`": $(F $p1y),")
    [void]$sb.AppendLine("        `"n2X`": $(F $p2x), `"n2Y`": $(F $p2y),")
    [void]$sb.AppendLine("        `"n3X`": $(F $p3x), `"n3Y`": $(F $p3y),")
    [void]$sb.AppendLine("        `"n4X`": 0, `"n4Y`": 0, `"n5X`": 0, `"n5Y`": 0,")
    [void]$sb.AppendLine("        `"n6X`": 0, `"n6Y`": 0, `"n7X`": 0, `"n7Y`": 0, `"n8X`": 0, `"n8Y`": 0,")
    [void]$sb.AppendLine("        `"kind`": `"T3`",")
    [void]$sb.AppendLine("        `"status`": `"Active`",")
    [void]$sb.AppendLine("        `"smithEmb2dLocalOrder`": false")
    [void]$sb.AppendLine("      }$comma")
}
[void]$sb.Append("    ]")
$meshInsert = $sb.ToString()

$bcSb = New-Object System.Text.StringBuilder
[void]$bcSb.AppendLine("    `"rows`": [")
[void]$bcSb.AppendLine("      {")
[void]$bcSb.AppendLine("        `"nodeId`": 1,")
[void]$bcSb.AppendLine("        `"boundaryName`": `"Node 1`",")
[void]$bcSb.AppendLine("        `"bcType`": `"Fixed`",")
[void]$bcSb.AppendLine("        `"analysisCategory`": `"Structural`",")
[void]$bcSb.AppendLine("        `"degreeOfFreedom`": 0,")
[void]$bcSb.AppendLine("        `"value`": `"0`",")
[void]$bcSb.AppendLine("        `"description`": `"Both DOFs restrained at (0,0)`"")
[void]$bcSb.AppendLine("      },")
for ($nid = 2; $nid -le $nxl; $nid++) {
    $comma = if ($nid -lt $nxl) { "," } else { "" }
    [void]$bcSb.AppendLine("      {")
    [void]$bcSb.AppendLine("        `"nodeId`": $nid,")
    [void]$bcSb.AppendLine("        `"boundaryName`": `"Node $nid`",")
    [void]$bcSb.AppendLine("        `"bcType`": `"Roller Y`",")
    [void]$bcSb.AppendLine("        `"analysisCategory`": `"Structural`",")
    [void]$bcSb.AppendLine("        `"degreeOfFreedom`": 1,")
    [void]$bcSb.AppendLine("        `"value`": `"0`",")
    [void]$bcSb.AppendLine("        `"description`": `"y restrained (roller on x-axis plane)`"")
    [void]$bcSb.AppendLine("      }$comma")
}
[void]$bcSb.Append("    ]")
$bcInsert = $bcSb.ToString()

$meshPattern = '(?s)"mesh": \{.*?\n  \},'
$meshReplacement = @"
"mesh": {
    "oneDMeshLength": "",
    "oneDMeshN": "",
    "oneDMeshStatus": "",
    "isDiscretized": true,
    "showMeshElements": true,
    "showMeshNodes": true,
    "selectedTwoDElementFamily": "Triangles",
    "selectedTwoDNodeOption": "3-node",
    "selectedMeshNumberingDirection": "dir = x",
    "globalDensityPreset": "Custom",
    "globalMeshSize": "0.25",
    "nearBoundaryMeshSize": "0.25",
    "surfaceMeshStatus": "JR-01 medium T3 (8x16, 256 triangles). Remesh before Run FE to split the joint.",
    "meshPopupSelectedTabIndex": 0,
$meshInsert
  },
"@

$bcPattern = '(?s)"boundaryConditions": \{.*?\n  \},'
$bcReplacement = @"
"boundaryConditions": {
$bcInsert,
    "statusMessage": "Base uy=0; (0,0) fixed. Shown on the Boundary Conditions canvas.",
    "nodesText": "",
    "selectedBCTypeForBoundary": "Fixed Y",
    "bcValueForBoundary": "0"
  },
"@

$newText = [regex]::Replace($text, $meshPattern, [System.Text.RegularExpressions.MatchEvaluator]{ param($m) $meshReplacement }, 1)
if ($newText -eq $text) { throw "Failed to replace mesh block" }
$text = $newText
$newText = [regex]::Replace($text, $bcPattern, [System.Text.RegularExpressions.MatchEvaluator]{ param($m) $bcReplacement }, 1)
if ($newText -eq $text) { throw "Failed to replace boundaryConditions block" }

[System.IO.File]::WriteAllText($path, $newText)
Write-Host "Wrote $nn nodes, $($tris.Count) T3s, $nxl BCs -> $path"
