<#
  Generates the app icon and the .pdf document icon as multi-size .ico files
  (PNG-compressed entries, 16-256 px). Each size is drawn separately so small sizes stay crisp.
  Output: src/LightPdf.App/Assets/app.ico and pdf-document.ico
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root   = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'src/LightPdf.App/Assets'
New-Item -ItemType Directory -Force $assets | Out-Null

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$blue  = [System.Drawing.Color]::FromArgb(255, 37, 99, 235)
$fold  = [System.Drawing.Color]::FromArgb(255, 191, 210, 245)
$line  = [System.Drawing.Color]::FromArgb(255, 148, 163, 184)
$edge  = [System.Drawing.Color]::FromArgb(255, 100, 116, 139)

function New-RoundedRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

# A page with a folded top-right corner. Returns nothing; draws into $g.
function Draw-Page($g, [single]$x, [single]$y, [single]$w, [single]$h, [single]$foldSize, $outline) {
    $page = New-Object System.Drawing.Drawing2D.GraphicsPath
    $page.AddLines([System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($x, $y),
        [System.Drawing.PointF]::new($x + $w - $foldSize, $y),
        [System.Drawing.PointF]::new($x + $w, $y + $foldSize),
        [System.Drawing.PointF]::new($x + $w, $y + $h),
        [System.Drawing.PointF]::new($x, $y + $h)))
    $page.CloseFigure()
    $g.FillPath([System.Drawing.Brushes]::White, $page)
    if ($outline) { $g.DrawPath($outline, $page) }
    $corner = New-Object System.Drawing.Drawing2D.GraphicsPath
    $corner.AddLines([System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($x + $w - $foldSize, $y),
        [System.Drawing.PointF]::new($x + $w - $foldSize, $y + $foldSize),
        [System.Drawing.PointF]::new($x + $w, $y + $foldSize)))
    $corner.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush $fold), $corner)
}

function New-Canvas([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)
    @($bmp, $g)
}

function Draw-AppIcon([int]$s) {
    $bmp, $g = New-Canvas $s
    $inset = [Math]::Max(0.5, $s * 0.04)
    $g.FillPath((New-Object System.Drawing.SolidBrush $blue), (New-RoundedRect $inset $inset ($s - 2 * $inset) ($s - 2 * $inset) ($s * 0.2)))
    $px = $s * 0.27; $py = $s * 0.17; $pw = $s * 0.46; $ph = $s * 0.66
    Draw-Page $g $px $py $pw $ph ($s * 0.14) $null
    $lines = if ($s -ge 32) { 4 } elseif ($s -ge 20) { 3 } else { 2 }
    $pen = New-Object System.Drawing.Pen $line, ([Math]::Max(1, $s * 0.045))
    for ($i = 0; $i -lt $lines; $i++) {
        $ly = $py + $ph * (0.38 + $i * (0.5 / $lines))
        $len = if ($i -eq $lines - 1) { 0.45 } else { 0.68 }
        $g.DrawLine($pen, $px + $pw * 0.16, $ly, $px + $pw * (0.16 + $len), $ly)
    }
    $g.Dispose(); $bmp
}

function Draw-DocumentIcon([int]$s) {
    $bmp, $g = New-Canvas $s
    $px = $s * 0.16; $py = $s * 0.05; $pw = $s * 0.68; $ph = $s * 0.9
    $outline = New-Object System.Drawing.Pen $edge, ([Math]::Max(1, $s / 32))
    Draw-Page $g $px $py $pw $ph ($s * 0.2) $outline
    # Blue label band across the lower part of the page.
    $bandH = $s * 0.26; $bandY = $py + $ph * 0.58
    $g.FillRectangle((New-Object System.Drawing.SolidBrush $blue), $s * 0.06, $bandY, $s * 0.72, $bandH)
    if ($s -ge 32) {
        $font = New-Object System.Drawing.Font 'Segoe UI', ($bandH * 0.62), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
        $fmt = New-Object System.Drawing.StringFormat
        $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
        $g.DrawString('PDF', $font, [System.Drawing.Brushes]::White,
            (New-Object System.Drawing.RectangleF ($s * 0.06), $bandY, ($s * 0.72), $bandH), $fmt)
    }
    $g.Dispose(); $bmp
}

# ICO = ICONDIR + one ICONDIRENTRY per image + the PNG payloads.
function Write-Ico([string]$path, [scriptblock]$draw) {
    $pngs = foreach ($s in $sizes) {
        $bmp = & $draw $s
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        , @($s, $ms.ToArray())
    }
    $out = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $out
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$pngs.Count)
    $offset = 6 + 16 * $pngs.Count
    foreach ($p in $pngs) {
        $s = $p[0]; $data = $p[1]
        $dim = if ($s -ge 256) { 0 } else { $s }      # 0 means 256
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32)       # planes, bits per pixel
        $w.Write([uint32]$data.Length); $w.Write([uint32]$offset)
        $offset += $data.Length
    }
    foreach ($p in $pngs) { $w.Write([byte[]]$p[1]) }
    $w.Flush()
    [System.IO.File]::WriteAllBytes($path, $out.ToArray())
    Write-Host ("{0}: {1} sizes, {2:N0} bytes" -f (Split-Path $path -Leaf), $pngs.Count, $out.Length)
}

Write-Ico (Join-Path $assets 'app.ico') ${function:Draw-AppIcon}
Write-Ico (Join-Path $assets 'pdf-document.ico') ${function:Draw-DocumentIcon}

# Previews for eyeballing (not shipped).
$preview = Join-Path ([IO.Path]::GetTempPath()) 'lightpdf-icon-preview.png'
$sheet = New-Object System.Drawing.Bitmap 560, 300
$g = [System.Drawing.Graphics]::FromImage($sheet)
$g.Clear([System.Drawing.Color]::FromArgb(255, 240, 240, 240))
$x = 10
foreach ($s in 16, 24, 32, 48, 64, 128) {
    $a = Draw-AppIcon $s;      $g.DrawImage($a, $x, 10, $s, $s)
    $d = Draw-DocumentIcon $s; $g.DrawImage($d, $x, 150, $s, $s)
    $x += $s + 14
}
$g.Dispose()
$sheet.Save($preview, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "preview: $preview"
