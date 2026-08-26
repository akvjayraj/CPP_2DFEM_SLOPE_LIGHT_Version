<#
.SYNOPSIS
  Convert docs/SmokeTest_ElementVerification.md into a Word (.docx) help file
  without requiring pandoc, Word, or Python. Produces valid Office Open XML
  via System.IO.Compression.

.DESCRIPTION
  Supports a subset of Markdown sufficient for professional geotechnical
  software help files:
    - Headings: '#', '##', '###'
    - Paragraphs (blank-line separated)
    - Pipe tables (with '|---|' separator row)
    - Fenced code blocks (triple backtick)
    - Bullet lists ('- ')
    - Blockquotes ('> ')
    - Horizontal rule ('---')
    - Inline code (backtick-delimited) and bold (**bold**)

.PARAMETER InputMd
  Path to the input Markdown source.

.PARAMETER OutputDocx
  Path to the output .docx file.
#>

[CmdletBinding()]
param(
    [string] $InputMd,
    [string] $OutputDocx
)

if (-not $InputMd -or -not $OutputDocx) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    if (-not $InputMd)    { $InputMd    = Join-Path $scriptDir '..\SmokeTest_ElementVerification.md' }
    if (-not $OutputDocx) { $OutputDocx = Join-Path $scriptDir '..\SmokeTest_ElementVerification.docx' }
}

Add-Type -AssemblyName System.IO.Compression -ErrorAction SilentlyContinue
Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue

# ---------------------------------------------------------------------------
# XML escaping and run helpers
# ---------------------------------------------------------------------------
function ConvertTo-Xml {
    param([string] $Text)
    if ($null -eq $Text) { return '' }
    $t = $Text.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;').Replace('"', '&quot;')
    return $t
}

# Convert a single-line fragment with inline **bold** and `code` segments
# into a sequence of <w:r> runs.
function New-InlineRuns {
    param([string] $Line)
    if ([string]::IsNullOrEmpty($Line)) {
        return '<w:r><w:t xml:space="preserve"></w:t></w:r>'
    }

    $tokens = [System.Collections.Generic.List[object]]::new()
    $i = 0
    $sb = New-Object System.Text.StringBuilder

    $flushPlain = {
        if ($sb.Length -gt 0) {
            [void] $tokens.Add([pscustomobject]@{ Kind = 'plain'; Text = $sb.ToString() })
            [void] $sb.Clear()
        }
    }

    while ($i -lt $Line.Length) {
        $ch = $Line[$i]
        if ($ch -eq '`') {
            & $flushPlain
            $j = $Line.IndexOf('`', $i + 1)
            if ($j -lt 0) {
                [void] $sb.Append($ch)
                $i++
            } else {
                [void] $tokens.Add([pscustomobject]@{ Kind = 'code'; Text = $Line.Substring($i + 1, $j - $i - 1) })
                $i = $j + 1
            }
        }
        elseif ($i -lt $Line.Length - 1 -and $Line.Substring($i, 2) -eq '**') {
            & $flushPlain
            $j = $Line.IndexOf('**', $i + 2)
            if ($j -lt 0) {
                [void] $sb.Append($ch)
                $i++
            } else {
                [void] $tokens.Add([pscustomobject]@{ Kind = 'bold'; Text = $Line.Substring($i + 2, $j - $i - 2) })
                $i = $j + 2
            }
        }
        else {
            [void] $sb.Append($ch)
            $i++
        }
    }
    & $flushPlain

    $runs = foreach ($tok in $tokens) {
        $esc = ConvertTo-Xml $tok.Text
        switch ($tok.Kind) {
            'bold' { "<w:r><w:rPr><w:b/></w:rPr><w:t xml:space=""preserve"">$esc</w:t></w:r>" }
            'code' { "<w:r><w:rPr><w:rFonts w:ascii=""Consolas"" w:hAnsi=""Consolas""/><w:sz w:val=""20""/><w:shd w:val=""clear"" w:color=""auto"" w:fill=""F2F2F2""/></w:rPr><w:t xml:space=""preserve"">$esc</w:t></w:r>" }
            default { "<w:r><w:t xml:space=""preserve"">$esc</w:t></w:r>" }
        }
    }
    return ($runs -join '')
}

function New-Paragraph {
    param(
        [string] $Text  = '',
        [string] $Style = '',
        [switch] $NoInline
    )
    $pPr = if ($Style) { "<w:pPr><w:pStyle w:val=""$Style""/></w:pPr>" } else { '' }
    if ($NoInline.IsPresent) {
        $esc = ConvertTo-Xml $Text
        return "<w:p>$pPr<w:r><w:t xml:space=""preserve"">$esc</w:t></w:r></w:p>"
    }
    $runs = New-InlineRuns -Line $Text
    return "<w:p>$pPr$runs</w:p>"
}

function New-Heading {
    param([string] $Text, [int] $Level)
    return (New-Paragraph -Text $Text -Style "Heading$Level")
}

function New-Bullet {
    param([string] $Text)
    return (New-Paragraph -Text $Text -Style 'ListBullet')
}

function New-Quote {
    param([string] $Text)
    return (New-Paragraph -Text $Text -Style 'Quote')
}

function New-HorizontalRule {
    return '<w:p><w:pPr><w:pBdr><w:bottom w:val="single" w:sz="8" w:space="1" w:color="808080"/></w:pBdr></w:pPr></w:p>'
}

function New-CodeBlock {
    param([string[]] $Lines)
    $body = foreach ($l in $Lines) {
        $esc = ConvertTo-Xml $l
        "<w:p><w:pPr><w:pStyle w:val=""CodeBlock""/></w:pPr><w:r><w:rPr><w:rFonts w:ascii=""Consolas"" w:hAnsi=""Consolas""/><w:sz w:val=""20""/></w:rPr><w:t xml:space=""preserve"">$esc</w:t></w:r></w:p>"
    }
    return ($body -join '')
}

function New-TableCell {
    param([string] $Text, [switch] $Header)
    $style   = if ($Header) { 'TableHeader' } else { 'TableCell' }
    $shading = if ($Header) { '<w:shd w:val="clear" w:color="auto" w:fill="D9E2F3"/>' } else { '' }
    $runs = New-InlineRuns -Line $Text
    return "<w:tc><w:tcPr><w:tcW w:w=""0"" w:type=""auto""/>$shading</w:tcPr><w:p><w:pPr><w:pStyle w:val=""$style""/></w:pPr>$runs</w:p></w:tc>"
}

function New-Table {
    param([string[][]] $Rows)
    if (-not $Rows -or $Rows.Count -eq 0) { return '' }
    $xml = New-Object System.Text.StringBuilder
    [void] $xml.Append('<w:tbl><w:tblPr><w:tblStyle w:val="SimpleTable"/><w:tblW w:w="5000" w:type="pct"/><w:tblBorders><w:top w:val="single" w:sz="4" w:color="808080"/><w:left w:val="single" w:sz="4" w:color="808080"/><w:bottom w:val="single" w:sz="4" w:color="808080"/><w:right w:val="single" w:sz="4" w:color="808080"/><w:insideH w:val="single" w:sz="4" w:color="808080"/><w:insideV w:val="single" w:sz="4" w:color="808080"/></w:tblBorders><w:tblLook w:val="04A0"/></w:tblPr>')
    $rowIndex = 0
    foreach ($r in $Rows) {
        [void] $xml.Append('<w:tr>')
        foreach ($c in $r) {
            if ($rowIndex -eq 0) {
                [void] $xml.Append((New-TableCell -Text $c -Header))
            } else {
                [void] $xml.Append((New-TableCell -Text $c))
            }
        }
        [void] $xml.Append('</w:tr>')
        $rowIndex++
    }
    [void] $xml.Append('</w:tbl>')
    # Empty paragraph after a table so Word does not merge table with following block
    [void] $xml.Append('<w:p/>')
    return $xml.ToString()
}

# ---------------------------------------------------------------------------
# Markdown parser (line-oriented)
# ---------------------------------------------------------------------------
function Split-MdTableRow {
    param([string] $Row)
    # Strip leading/trailing pipes then split
    $t = $Row.Trim()
    if ($t.StartsWith('|')) { $t = $t.Substring(1) }
    if ($t.EndsWith('|'))   { $t = $t.Substring(0, $t.Length - 1) }
    return ($t -split '\|') | ForEach-Object { $_.Trim() }
}

function Test-MdTableSeparator {
    param([string] $Row)
    $cells = Split-MdTableRow -Row $Row
    if ($cells.Count -eq 0) { return $false }
    foreach ($c in $cells) {
        if ($c -notmatch '^:?-{3,}:?$') { return $false }
    }
    return $true
}

function ConvertFrom-MarkdownToOoxml {
    param([string[]] $Lines)

    $out = New-Object System.Text.StringBuilder
    $i = 0
    $n = $Lines.Length

    # Paragraph accumulator
    $paraLines = New-Object System.Collections.Generic.List[string]
    $flushPara = {
        if ($paraLines.Count -gt 0) {
            $text = ($paraLines -join ' ').Trim()
            if ($text.Length -gt 0) {
                [void] $out.Append((New-Paragraph -Text $text))
            }
            $paraLines.Clear()
        }
    }

    while ($i -lt $n) {
        $line = $Lines[$i]
        $trim = $line.TrimEnd()

        # Blank line => flush paragraph
        if ([string]::IsNullOrWhiteSpace($trim)) {
            &$flushPara
            $i++
            continue
        }

        # Fenced code block
        if ($trim.StartsWith('```')) {
            &$flushPara
            $i++
            $codeLines = New-Object System.Collections.Generic.List[string]
            while ($i -lt $n -and -not $Lines[$i].TrimStart().StartsWith('```')) {
                $codeLines.Add($Lines[$i])
                $i++
            }
            if ($i -lt $n) { $i++ }   # consume closing fence
            [void] $out.Append((New-CodeBlock -Lines $codeLines.ToArray()))
            continue
        }

        # Heading
        if ($trim -match '^(#{1,6})\s+(.*)$') {
            &$flushPara
            $level  = [math]::Min($Matches[1].Length, 3)
            $htext  = $Matches[2].Trim()
            [void] $out.Append((New-Heading -Text $htext -Level $level))
            $i++
            continue
        }

        # Horizontal rule
        if ($trim -match '^-{3,}\s*$') {
            &$flushPara
            [void] $out.Append((New-HorizontalRule))
            $i++
            continue
        }

        # Blockquote
        if ($trim.StartsWith('>')) {
            &$flushPara
            $qtext = $trim.Substring(1).TrimStart()
            [void] $out.Append((New-Quote -Text $qtext))
            $i++
            continue
        }

        # Bullet list item
        if ($trim -match '^\s*[-*]\s+(.*)$') {
            &$flushPara
            $btext = $Matches[1]
            [void] $out.Append((New-Bullet -Text $btext))
            $i++
            continue
        }

        # Pipe table: header row followed by separator
        if ($trim.StartsWith('|') -and ($i + 1) -lt $n -and (Test-MdTableSeparator -Row $Lines[$i + 1])) {
            &$flushPara
            $rows = @()
            $rows += ,(Split-MdTableRow -Row $Lines[$i])        # header
            $i += 2                                             # skip separator
            while ($i -lt $n -and $Lines[$i].TrimEnd().StartsWith('|')) {
                $rows += ,(Split-MdTableRow -Row $Lines[$i])
                $i++
            }
            [void] $out.Append((New-Table -Rows $rows))
            continue
        }

        # Default: accumulate into current paragraph
        $paraLines.Add($trim)
        $i++
    }

    &$flushPara
    return $out.ToString()
}

# ---------------------------------------------------------------------------
# Static OOXML package parts
# ---------------------------------------------------------------------------
$contentTypes = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
  <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
  <Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
  <Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>
</Types>
'@

$rootRels = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>
  <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/>
</Relationships>
'@

$documentRels = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="sId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
</Relationships>
'@

$styles = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:docDefaults>
    <w:rPrDefault>
      <w:rPr>
        <w:rFonts w:ascii="Calibri" w:hAnsi="Calibri" w:cs="Calibri"/>
        <w:sz w:val="22"/>
        <w:szCs w:val="22"/>
      </w:rPr>
    </w:rPrDefault>
    <w:pPrDefault>
      <w:pPr>
        <w:spacing w:after="120" w:line="288" w:lineRule="auto"/>
      </w:pPr>
    </w:pPrDefault>
  </w:docDefaults>

  <w:style w:type="paragraph" w:styleId="Normal" w:default="1">
    <w:name w:val="Normal"/>
  </w:style>

  <w:style w:type="paragraph" w:styleId="Heading1">
    <w:name w:val="heading 1"/>
    <w:next w:val="Normal"/>
    <w:pPr>
      <w:spacing w:before="360" w:after="180"/>
      <w:outlineLvl w:val="0"/>
    </w:pPr>
    <w:rPr>
      <w:rFonts w:ascii="Calibri Light" w:hAnsi="Calibri Light"/>
      <w:color w:val="1F3864"/>
      <w:sz w:val="40"/>
      <w:szCs w:val="40"/>
      <w:b/>
    </w:rPr>
  </w:style>

  <w:style w:type="paragraph" w:styleId="Heading2">
    <w:name w:val="heading 2"/>
    <w:next w:val="Normal"/>
    <w:pPr>
      <w:spacing w:before="280" w:after="140"/>
      <w:outlineLvl w:val="1"/>
    </w:pPr>
    <w:rPr>
      <w:rFonts w:ascii="Calibri Light" w:hAnsi="Calibri Light"/>
      <w:color w:val="2E74B5"/>
      <w:sz w:val="32"/>
      <w:szCs w:val="32"/>
      <w:b/>
    </w:rPr>
  </w:style>

  <w:style w:type="paragraph" w:styleId="Heading3">
    <w:name w:val="heading 3"/>
    <w:next w:val="Normal"/>
    <w:pPr>
      <w:spacing w:before="220" w:after="120"/>
      <w:outlineLvl w:val="2"/>
    </w:pPr>
    <w:rPr>
      <w:rFonts w:ascii="Calibri Light" w:hAnsi="Calibri Light"/>
      <w:color w:val="1F4E79"/>
      <w:sz w:val="26"/>
      <w:szCs w:val="26"/>
      <w:b/>
    </w:rPr>
  </w:style>

  <w:style w:type="paragraph" w:styleId="Quote">
    <w:name w:val="Quote"/>
    <w:basedOn w:val="Normal"/>
    <w:pPr>
      <w:spacing w:before="120" w:after="120"/>
      <w:ind w:left="360"/>
      <w:pBdr><w:left w:val="single" w:sz="18" w:space="8" w:color="7F9CCB"/></w:pBdr>
    </w:pPr>
    <w:rPr>
      <w:i/>
      <w:color w:val="44546A"/>
    </w:rPr>
  </w:style>

  <w:style w:type="paragraph" w:styleId="ListBullet">
    <w:name w:val="List Bullet"/>
    <w:basedOn w:val="Normal"/>
    <w:pPr>
      <w:ind w:left="720" w:hanging="360"/>
    </w:pPr>
  </w:style>

  <w:style w:type="paragraph" w:styleId="CodeBlock">
    <w:name w:val="Code Block"/>
    <w:basedOn w:val="Normal"/>
    <w:pPr>
      <w:spacing w:before="60" w:after="60"/>
      <w:ind w:left="280"/>
      <w:pBdr><w:top w:val="single" w:sz="4" w:color="BFBFBF"/><w:left w:val="single" w:sz="4" w:color="BFBFBF"/><w:bottom w:val="single" w:sz="4" w:color="BFBFBF"/><w:right w:val="single" w:sz="4" w:color="BFBFBF"/></w:pBdr>
      <w:shd w:val="clear" w:color="auto" w:fill="F7F7F7"/>
    </w:pPr>
    <w:rPr>
      <w:rFonts w:ascii="Consolas" w:hAnsi="Consolas"/>
      <w:sz w:val="20"/>
    </w:rPr>
  </w:style>

  <w:style w:type="paragraph" w:styleId="TableHeader">
    <w:name w:val="Table Header"/>
    <w:basedOn w:val="Normal"/>
    <w:pPr>
      <w:spacing w:before="40" w:after="40"/>
    </w:pPr>
    <w:rPr>
      <w:b/>
      <w:color w:val="1F3864"/>
    </w:rPr>
  </w:style>

  <w:style w:type="paragraph" w:styleId="TableCell">
    <w:name w:val="Table Cell"/>
    <w:basedOn w:val="Normal"/>
    <w:pPr>
      <w:spacing w:before="40" w:after="40"/>
    </w:pPr>
  </w:style>

  <w:style w:type="table" w:styleId="SimpleTable">
    <w:name w:val="Simple Table"/>
    <w:tblPr>
      <w:tblBorders>
        <w:top w:val="single" w:sz="4" w:color="808080"/>
        <w:left w:val="single" w:sz="4" w:color="808080"/>
        <w:bottom w:val="single" w:sz="4" w:color="808080"/>
        <w:right w:val="single" w:sz="4" w:color="808080"/>
        <w:insideH w:val="single" w:sz="4" w:color="808080"/>
        <w:insideV w:val="single" w:sz="4" w:color="808080"/>
      </w:tblBorders>
    </w:tblPr>
  </w:style>
</w:styles>
'@

$now = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
$coreProps = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <dc:title>2D FEM Element Smoke Test Verification</dc:title>
  <dc:subject>T3, T6, Q4, Q8 smoke tests</dc:subject>
  <dc:creator>X2D FE Analysis</dc:creator>
  <cp:keywords>FEM, smoke test, T3, T6, Q4, Q8, Kirsch, Salencon</cp:keywords>
  <dc:description>Professional help and verification reference document for 2D FEM element smoke tests.</dc:description>
  <cp:lastModifiedBy>X2D FE Analysis</cp:lastModifiedBy>
  <dcterms:created xsi:type="dcterms:W3CDTF">$now</dcterms:created>
  <dcterms:modified xsi:type="dcterms:W3CDTF">$now</dcterms:modified>
</cp:coreProperties>
"@

$appProps = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties" xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
  <Application>X2D FE Analysis</Application>
  <AppVersion>1.0</AppVersion>
</Properties>
'@

# ---------------------------------------------------------------------------
# Build document.xml from the Markdown source
# ---------------------------------------------------------------------------
if (-not (Test-Path $InputMd)) {
    throw "Markdown source not found: $InputMd"
}

$mdLines = [System.IO.File]::ReadAllLines($InputMd)
$body    = ConvertFrom-MarkdownToOoxml -Lines $mdLines

$document = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:body>
    $body
    <w:sectPr>
      <w:pgSz w:w="12240" w:h="15840"/>
      <w:pgMar w:top="1200" w:right="1200" w:bottom="1200" w:left="1200" w:header="720" w:footer="720" w:gutter="0"/>
    </w:sectPr>
  </w:body>
</w:document>
"@

# ---------------------------------------------------------------------------
# Pack into .docx (ZIP) using System.IO.Compression
# ---------------------------------------------------------------------------
if (Test-Path $OutputDocx) { Remove-Item -Force $OutputDocx }

$fs  = [System.IO.File]::Open($OutputDocx, [System.IO.FileMode]::Create)
$zip = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)

function Add-ZipEntry {
    param(
        [Parameter(Mandatory)] $Archive,
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Content
    )
    $entry  = $Archive.CreateEntry($Name, [System.IO.Compression.CompressionLevel]::Optimal)
    $stream = $entry.Open()
    $writer = New-Object System.IO.StreamWriter($stream, [System.Text.UTF8Encoding]::new($false))
    $writer.Write($Content)
    $writer.Dispose()
    $stream.Dispose()
}

Add-ZipEntry -Archive $zip -Name '[Content_Types].xml'           -Content $contentTypes
Add-ZipEntry -Archive $zip -Name '_rels/.rels'                   -Content $rootRels
Add-ZipEntry -Archive $zip -Name 'word/document.xml'             -Content $document
Add-ZipEntry -Archive $zip -Name 'word/_rels/document.xml.rels'  -Content $documentRels
Add-ZipEntry -Archive $zip -Name 'word/styles.xml'               -Content $styles
Add-ZipEntry -Archive $zip -Name 'docProps/core.xml'             -Content $coreProps
Add-ZipEntry -Archive $zip -Name 'docProps/app.xml'              -Content $appProps

$zip.Dispose()
$fs.Dispose()

Write-Host "Generated $OutputDocx"
