$ErrorActionPreference = "Stop"
$case = Split-Path -Parent $PSScriptRoot
$path = Join-Path $case "jr03_single_joint_column_axial.xfem"

$nx = 8
$ny = 24
$nxl = $nx + 1
$nyl = $ny + 1
$dx = 1.0 / $nx
$dy = 3.0 / $ny

function NodeId([int]$ix, [int]$iy) { return $iy * $nxl + $ix + 1 }

function F([double]$v) {
    return $v.ToString("0.########", [System.Globalization.CultureInfo]::InvariantCulture)
}

$nn = $nxl * $nyl
$nodeX = New-Object 'double[]' $nn
$nodeY = New-Object 'double[]' $nn
for ($iy = 0; $iy -lt $nyl; $iy++) {
    for ($ix = 0; $ix -lt $nxl; $ix++) {
        $id = (NodeId $ix $iy) - 1
        $nodeX[$id] = $ix * $dx
        $nodeY[$id] = $iy * $dy
    }
}

$tris = New-Object System.Collections.Generic.List[object]
function Add-Tri([int]$n1, [int]$n2, [int]$n3) {
    $tris.Add([pscustomobject]@{ n1 = $n1; n2 = $n2; n3 = $n3 })
}

for ($iy = 0; $iy -lt $ny; $iy++) {
    for ($ix = 0; $ix -lt $nx; $ix++) {
        $n00 = NodeId $ix $iy
        $n10 = NodeId ($ix + 1) $iy
        $n11 = NodeId ($ix + 1) ($iy + 1)
        $n01 = NodeId $ix ($iy + 1)
        Add-Tri $n00 $n10 $n11
        Add-Tri $n00 $n11 $n01
    }
}

$meshSb = New-Object System.Text.StringBuilder
[void]$meshSb.AppendLine("    `"discretizationNodes`": [")
for ($i = 0; $i -lt $nn; $i++) {
    $comma = if ($i -lt $nn - 1) { "," } else { "" }
    [void]$meshSb.AppendLine("      { `"x`": $(F $nodeX[$i]), `"y`": $(F $nodeY[$i]) }$comma")
}
[void]$meshSb.AppendLine("    ],")

[void]$meshSb.AppendLine("    `"elements`": [")
for ($e = 0; $e -lt $tris.Count; $e++) {
    $t = $tris[$e]
    $p1x = $nodeX[$t.n1 - 1]; $p1y = $nodeY[$t.n1 - 1]
    $p2x = $nodeX[$t.n2 - 1]; $p2y = $nodeY[$t.n2 - 1]
    $p3x = $nodeX[$t.n3 - 1]; $p3y = $nodeY[$t.n3 - 1]
    $cx = ($p1x + $p2x + $p3x) / 3.0
    $cy = ($p1y + $p2y + $p3y) / 3.0
    $comma = if ($e -lt $tris.Count - 1) { "," } else { "" }
    [void]$meshSb.AppendLine("      {")
    [void]$meshSb.AppendLine("        `"displayId`": $($e + 1),")
    [void]$meshSb.AppendLine("        `"isActive`": true,")
    [void]$meshSb.AppendLine("        `"isHighlighted`": false,")
    [void]$meshSb.AppendLine("        `"isQuadraticTriangle`": false,")
    [void]$meshSb.AppendLine("        `"isQuadrilateral`": false,")
    [void]$meshSb.AppendLine("        `"isQuadraticQuad`": false,")
    [void]$meshSb.AppendLine("        `"isUnmatchedTriangle`": false,")
    [void]$meshSb.AppendLine("        `"centerX`": $(F $cx),")
    [void]$meshSb.AppendLine("        `"centerY`": $(F $cy),")
    [void]$meshSb.AppendLine("        `"nodePoints`": [")
    [void]$meshSb.AppendLine("          { `"x`": $(F $p1x), `"y`": $(F $p1y) },")
    [void]$meshSb.AppendLine("          { `"x`": $(F $p2x), `"y`": $(F $p2y) },")
    [void]$meshSb.AppendLine("          { `"x`": $(F $p3x), `"y`": $(F $p3y) }")
    [void]$meshSb.AppendLine("        ]")
    [void]$meshSb.AppendLine("      }$comma")
}
[void]$meshSb.AppendLine("    ],")

[void]$meshSb.AppendLine("    `"informationRows`": [")
for ($e = 0; $e -lt $tris.Count; $e++) {
    $t = $tris[$e]
    $p1x = $nodeX[$t.n1 - 1]; $p1y = $nodeY[$t.n1 - 1]
    $p2x = $nodeX[$t.n2 - 1]; $p2y = $nodeY[$t.n2 - 1]
    $p3x = $nodeX[$t.n3 - 1]; $p3y = $nodeY[$t.n3 - 1]
    $comma = if ($e -lt $tris.Count - 1) { "," } else { "" }
    [void]$meshSb.AppendLine("      {")
    [void]$meshSb.AppendLine("        `"elementId`": $($e + 1),")
    [void]$meshSb.AppendLine("        `"node1`": $($t.n1), `"node2`": $($t.n2), `"node3`": $($t.n3),")
    [void]$meshSb.AppendLine("        `"node4`": 0, `"node5`": 0, `"node6`": 0, `"node7`": 0, `"node8`": 0,")
    [void]$meshSb.AppendLine("        `"etype`": 1,")
    [void]$meshSb.AppendLine("        `"n1X`": $(F $p1x), `"n1Y`": $(F $p1y),")
    [void]$meshSb.AppendLine("        `"n2X`": $(F $p2x), `"n2Y`": $(F $p2y),")
    [void]$meshSb.AppendLine("        `"n3X`": $(F $p3x), `"n3Y`": $(F $p3y),")
    [void]$meshSb.AppendLine("        `"n4X`": 0, `"n4Y`": 0, `"n5X`": 0, `"n5Y`": 0,")
    [void]$meshSb.AppendLine("        `"n6X`": 0, `"n6Y`": 0, `"n7X`": 0, `"n7Y`": 0, `"n8X`": 0, `"n8Y`": 0,")
    [void]$meshSb.AppendLine("        `"kind`": `"T3`",")
    [void]$meshSb.AppendLine("        `"status`": `"Active`",")
    [void]$meshSb.AppendLine("        `"smithEmb2dLocalOrder`": false")
    [void]$meshSb.AppendLine("      }$comma")
}
[void]$meshSb.Append("    ]")
$meshInsert = $meshSb.ToString()

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

$json = @"
{
  "formatVersion": "1.10",
  "main": {
    "selectedTabIndex": 0,
    "zoomPanModeEnabled": false
  },
  "projectSettings": {
    "selectedDimension": "twoD",
    "selectedSettingsTabIndex": 0,
    "isDeterministicAnalysis": true,
    "isProbabilisticAnalysis": false,
    "numberOfStages": 0,
    "stageFooterTabs": [],
    "projectName": "JR-03 - RS3 Case 1 Single Jointed Rock Column Under Axial Pressure",
    "author": "Verification",
    "createdOn": "2026-08-16T00:00:00",
    "units": "SI",
    "bishopMaxIterations": 100,
    "bishopConvergenceTolerance": 1E-06,
    "bishopInitialFactor": 1,
    "numberOfStagesModel": 1,
    "axisXMin": -0.2,
    "axisXMax": 1.2,
    "axisYMin": -0.2,
    "axisYMax": 3.2,
    "gridSpacingMeters": 0.25,
    "showGrid": true,
    "showAxes": true
  },
  "geometry": {
    "canvasWidth": 600,
    "canvasHeight": 400,
    "axisXMin": -0.2,
    "axisXMax": 1.2,
    "axisYMin": -0.2,
    "axisYMax": 3.2,
    "layerElevationInput": "",
    "hasAddedElevationLayer": false,
    "surfaceMaterialFills": [
      { "surfaceNumber": 1, "argb": 4288322202, "materialId": 1 },
      { "surfaceNumber": 2, "argb": 4288322202, "materialId": 1 }
    ],
    "tunnelCenterXInput": "",
    "tunnelCenterYInput": "",
    "tunnelRadiusInput": "",
    "geometryXInput": "",
    "geometryYInput": "",
    "vertices": [
      { "x": 0, "y": 0, "stablePointId": 1 },
      { "x": 1, "y": 0, "stablePointId": 2 },
      { "x": 1, "y": 1.5, "stablePointId": 3 },
      { "x": 0, "y": 1.5, "stablePointId": 4 },
      { "x": 1, "y": 3, "stablePointId": 5 },
      { "x": 0, "y": 3, "stablePointId": 6 }
    ],
    "lineSegments": [
      { "lineNumber": 1, "fromPoint": "Point 1", "toPoint": "Point 2", "isBoundaryEdge": true, "isArc": false, "isApplied": false },
      { "lineNumber": 2, "fromPoint": "Point 2", "toPoint": "Point 3", "isBoundaryEdge": true, "isArc": false, "isApplied": false },
      { "lineNumber": 3, "fromPoint": "Point 3", "toPoint": "Point 4", "isBoundaryEdge": true, "isArc": false, "isApplied": false },
      { "lineNumber": 4, "fromPoint": "Point 4", "toPoint": "Point 1", "isBoundaryEdge": true, "isArc": false, "isApplied": false },
      { "lineNumber": 5, "fromPoint": "Point 3", "toPoint": "Point 5", "isBoundaryEdge": true, "isArc": false, "isApplied": false },
      { "lineNumber": 6, "fromPoint": "Point 5", "toPoint": "Point 6", "isBoundaryEdge": true, "isArc": false, "isApplied": false },
      { "lineNumber": 7, "fromPoint": "Point 6", "toPoint": "Point 4", "isBoundaryEdge": true, "isArc": false, "isApplied": false }
    ],
    "internalEdges": [],
    "geometryCircles": [],
    "surfaces": [
      {
        "surfaceNumber": 1,
        "surfaceName": "Lower rock",
        "selectedLineNumbers": [1, 2, 3, 4],
        "colorIndex": 0,
        "assignedMaterialId": 1,
        "isPolygonLocked": true,
        "lockedPolygonVertexLabels": ["Point 1", "Point 2", "Point 3", "Point 4"],
        "connectedLineNumbers": [1, 2, 3, 4]
      },
      {
        "surfaceNumber": 2,
        "surfaceName": "Upper rock",
        "selectedLineNumbers": [3, 5, 6, 7],
        "colorIndex": 1,
        "assignedMaterialId": 1,
        "isPolygonLocked": true,
        "lockedPolygonVertexLabels": ["Point 4", "Point 3", "Point 5", "Point 6"],
        "connectedLineNumbers": [3, 5, 6, 7]
      }
    ],
    "selectedSurfaceNumber": 1,
    "tunnelRows": [],
    "groundwaterLines": [],
    "groundwaterEnabled": false,
    "groundwaterGammaW": 9.81,
    "groundwaterSurfPoints": [],
    "excavationPolygonsAxis": [],
    "surfaceExcavationOperations": [],
    "geologicalFeatures": [
      {
        "id": "jr03-joint",
        "kind": "WeakPlane",
        "name": "JR-03 horizontal joint at y=1.5 m",
        "isEnabled": true,
        "dipAngleDeg": 0,
        "knKpa": 10000000,
        "ksKpa": 10000000,
        "jointCohesionKpa": 0,
        "jointFrictionDeg": 89,
        "jointTensionCutoffKpa": 0,
        "propertySetId": 1,
        "endCondition": "BothOpen",
        "installStage": 1,
        "vertices": [
          { "x": 0, "y": 1.5 },
          { "x": 1, "y": 1.5 }
        ]
      }
    ],
    "jointPropertySets": [
      {
        "id": 1,
        "name": "JR-03 RS3 Case 1",
        "colourArgb": 4293441548,
        "criterion": "MohrCoulomb",
        "tensileStrengthKpa": 0,
        "peakCohesionKpa": 0,
        "peakFrictionDeg": 89,
        "useResidualStrength": false,
        "residualTensileStrengthKpa": 0,
        "residualCohesionKpa": 0,
        "residualFrictionDeg": 0,
        "normalStiffnessKpaPerM": 10000000,
        "shearStiffnessKpaPerM": 10000000,
        "useGroundwaterPorePressure": false,
        "useAdditionalPressure": false,
        "additionalPressureKpa": 0,
        "applyPressureToLinerSideOnly": false,
        "useStagedProperties": false,
        "allowSlipFromStage": 1,
        "initialJointDeformation": false
      }
    ],
    "jointNetworks": []
  },
  "materials": {
    "selectedFailureCriterion": "elastic",
    "selectedMaterialType": "isotropic",
    "selectedAnalysisCategory": "Structural – static elasticity",
    "selectedDetailedAnalysisType": "Plane strain",
    "showDeterministicTab": true,
    "showProbabilisticTab": false,
    "selectedMaterialLibraryId": 1,
    "materialLibrary": [
      {
        "id": 1,
        "name": "JR-03 elastic rock",
        "description": "RS3 Case 1: E=2000 MPa, nu=0.01, gamma=0",
        "colourArgb": 4288322202,
        "failureCriterion": "elastic",
        "materialType": "isotropic",
        "youngsModulus": 2000000,
        "poissonsRatio": 0.01,
        "youngsModulusH": 2000000,
        "youngsModulusV": 2000000,
        "poissonsRatioHH": 0.01,
        "poissonsRatioVH": 0.01,
        "shearModulusVH": 990099.0099,
        "unitWeight": 0,
        "saturatedUnitWeight": 0,
        "isSaturatedEnabled": false,
        "cohesion": 1E+10,
        "frictionAngle": 89,
        "dilationAngle": 0,
        "tensileCutoff": 0,
        "ucs": 1E+10,
        "hoekBrownMi": 10,
        "gsi": 100,
        "disturbance": 0,
        "modulusRatio": 400,
        "dpAlpha": 0,
        "dpK": 0,
        "permeabilityX": 1E-06,
        "permeabilityY": 1E-06,
        "porosity": 0.3,
        "initialVoidRatio": 0.5,
        "specificStorage": 0.0001,
        "thermalConductivityX": 2,
        "thermalConductivityY": 2,
        "heatCapacity": 1000,
        "thermalExpansion": 1E-05,
        "density": 0,
        "dampingRatio": 0.05
      }
    ]
  },
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
    "globalMeshSize": "0.125",
    "nearBoundaryMeshSize": "0.125",
    "surfaceMeshStatus": "JR-03 medium T3 (8x24). Horizontal joint at y=1.5 m. Remesh before Run FE to split the joint.",
    "meshPopupSelectedTabIndex": 0,
$meshInsert
  },
  "loads": [
    {
      "type": "line",
      "direction": "y",
      "magnitude": 1000,
      "endMagnitude": 1000,
      "startX": 0,
      "startY": 3,
      "endX": 1,
      "endY": 3,
      "name": "JR-03 top compression 1 MPa",
      "orientation": "verticalDown",
      "isEnabled": true
    }
  ],
  "boundaryConditions": {
$bcInsert,
    "statusMessage": "Base uy=0; (0,0) fixed. Shown on the Boundary Conditions canvas.",
    "nodesText": "",
    "selectedBCTypeForBoundary": "Fixed Y",
    "bcValueForBoundary": "0"
  },
  "feAnalysis": {
    "statusMessage": "JR-03 p610 T3, layers=0, gravity off. RS3 Case 1: P=1 MPa, E=2000 MPa, kn=10 GPa/m.",
    "feAnalysisSubTabIndex": 0,
    "stagedExcavationEnabled": false,
    "gravityEnabled": false,
    "gravityAcceleration": 9.81,
    "loadSteppingEnabled": true,
    "numberOfLoadSteps": 5,
    "convergenceTolerance": 0.0001,
    "maxIterations": 80,
    "loadIncrements": 5,
    "layersOverride": 0
  }
}
"@

[System.IO.File]::WriteAllText($path, $json)
Write-Host "Wrote $nn nodes, $($tris.Count) T3s, $nxl BCs -> $path"
