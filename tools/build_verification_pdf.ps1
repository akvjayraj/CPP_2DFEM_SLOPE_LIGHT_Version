<#
.SYNOPSIS
  Build PDF help from markdown via DOCX (best-effort).

.DESCRIPTION
  Converts Markdown -> DOCX using docs/tools/build_smoke_help_docx.ps1.
  Then attempts DOCX -> PDF using Microsoft Word COM automation on Windows.
  If Word is unavailable, the script exits successfully after DOCX generation
  and prints a warning so CI/local runs remain stable.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/build_verification_pdf.ps1 `
    -InputMd Verification\mc_tunnel\MC_Tunnel_All_SmokeTests_Help.md `
    -OutputPdf Verification\mc_tunnel\MC_Tunnel_All_SmokeTests_Help.pdf
#>

[CmdletBinding()]
param(
    [string] $InputMd,
    [string] $OutputPdf
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir '..')
$docxBuilder = Join-Path $repoRoot 'docs\tools\build_smoke_help_docx.ps1'

if (-not (Test-Path $docxBuilder)) {
    throw "DOCX builder not found: $docxBuilder"
}
if (-not $InputMd) {
    throw "InputMd is required."
}
if (-not (Test-Path $InputMd)) {
    throw "Input markdown not found: $InputMd"
}

$inputMdAbs = Resolve-Path $InputMd
if (-not $OutputPdf) {
    $OutputPdf = [System.IO.Path]::ChangeExtension($inputMdAbs, '.pdf')
}

$outputPdfAbs = [System.IO.Path]::GetFullPath($OutputPdf)
$outputDocxAbs = [System.IO.Path]::ChangeExtension($outputPdfAbs, '.docx')

# Step 1: Markdown -> DOCX
& powershell -ExecutionPolicy Bypass -File $docxBuilder -InputMd $inputMdAbs -OutputDocx $outputDocxAbs

# Step 2: DOCX -> PDF (best effort)
try {
    $word = New-Object -ComObject Word.Application
    $word.Visible = $false
    $word.DisplayAlerts = 0
    $doc = $word.Documents.Open($outputDocxAbs)
    try {
        # wdFormatPDF = 17
        $doc.SaveAs([ref] $outputPdfAbs, [ref] 17)
    } finally {
        $doc.Close()
    }
    $word.Quit()
    [System.Runtime.Interopservices.Marshal]::ReleaseComObject($doc) | Out-Null
    [System.Runtime.Interopservices.Marshal]::ReleaseComObject($word) | Out-Null
    Write-Host "Generated PDF: $outputPdfAbs"
}
catch {
    Write-Warning "DOCX generated but PDF conversion skipped. Microsoft Word COM automation is unavailable."
    Write-Warning "DOCX available at: $outputDocxAbs"
}
