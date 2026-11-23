<#
  Generates the app icon (a feather on a dark tile: "light" as in lightweight) and the .pdf document
  icon as multi-size .ico files (PNG-compressed entries, 16-256 px). Each size is drawn separately so
  small sizes stay crisp.

  -Variant  feather color scheme: night (silver on navy-charcoal), ember (gold on dark brown-black),
            teal (mint on dark slate)
  -Preview  only write a comparison sheet of all variants to dist/icon-options.png

  Output: src/LightPdf.App/Assets/app.ico and pdf-document.ico
#>
param(
    [ValidateSet('night', 'ember', 'teal')] [string]$Variant = 'night',
    [switch]$Preview
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root   = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'src/LightPdf.App/Assets'
New-Item -ItemType Directory -Force $assets | Out-Null

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
function Rgb([int]$r, [int]$g, [int]$b, [int]$a = 255) { [System.Drawing.Color]::FromArgb($a, $r, $g, $b) }

# Background top/bottom, feather tip/base, shaft.
$palettes = @{
    night = @{ BgTop = Rgb 38 46 64;  BgBottom = Rgb 14 18 27; Tip = Rgb 241 245 252; Base = Rgb 150 172 210; Shaft = Rgb 58 70 96 }
    ember = @{ BgTop = Rgb 46 34 28;  BgBottom = Rgb 16 12 10; Tip = Rgb 255 222 140; Base = Rgb 222 134 50;  Shaft = Rgb 92 54 26 }
    teal  = @{ BgTop = Rgb 24 48 50;  BgBottom = Rgb 8 20 22;  Tip = Rgb 200 255 240; Base = Rgb 32 178 160;  Shaft = Rgb 18 84 78 }
}

# Document icon colors (unchanged).
$blue = Rgb 37 99 235
$fold = Rgb 191 210 245
$edge = Rgb 100 116 139

function PtF([single]$x, [single]$y) { [System.Drawing.PointF]::new($x, $y) }

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

function New-Canvas([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)
    @($bmp, $g)
}

# ---------------------------------------------------------------- app icon: feather

# The feather is designed lying along +x (quill at x=0, tip at x=1), vane above (y<0) and below (y>0),
# then rotated 45° to point up-right and scaled to the tile.
function Draw-AppIcon([int]$s, [string]$variant = $Variant) {
    $c = $palettes[$variant]
    $bmp, $g = New-Canvas $s
    $small = $s -le 24

    # Dark tile with a faint light rim, so it stays visible on a dark taskbar.
    $inset = [Math]::Max(0.5, $s * 0.04)
    $tile = New-RoundedRect $inset $inset ($s - 2 * $inset) ($s - 2 * $inset) ($s * 0.22)
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (PtF 0 0), (PtF 0 $s), $c.BgTop, $c.BgBottom
    $g.FillPath($bg, $tile)
    if ($s -ge 24) {
        $rim = New-Object System.Drawing.Pen (Rgb 255 255 255 ([int]($small ? 40 : 28))), ([Math]::Max(1, $s / 128))
        $g.DrawPath($rim, $tile)
    }

    # Local → pixels: center the feather, rotate to point up-right, scale to ~80% of the tile.
    $m = New-Object System.Drawing.Drawing2D.Matrix
    $m.Translate($s * 0.5, $s * 0.52)
    $m.Rotate(-45)
    $m.Scale($s * 0.80, $s * 0.80 * ($small ? 1.35 : 1.0))   # chunkier vane when tiny
    $m.Translate(-0.46, 0)

    # Vane: two Bézier edges meeting at the tip.
    $vane = New-Object System.Drawing.Drawing2D.GraphicsPath
    $vane.AddBezier((PtF 0.10 0), (PtF 0.26 -0.21), (PtF 0.74 -0.21), (PtF 1.0 0))
    $vane.AddBezier((PtF 1.0 0), (PtF 0.76 0.16), (PtF 0.34 0.18), (PtF 0.15 0.0))
    $vane.CloseFigure()
    $region = New-Object System.Drawing.Region $vane
    if (-not $small) {
        # Two notches, slanted towards the tip like separated barbs.
        foreach ($notch in @(
            @((PtF 0.50 -0.30), (PtF 0.62 -0.30), (PtF 0.53 -0.075)),
            @((PtF 0.30 0.30), (PtF 0.41 0.30), (PtF 0.34 0.07)))) {
            $n = New-Object System.Drawing.Drawing2D.GraphicsPath
            $n.AddPolygon([System.Drawing.PointF[]]$notch)
            $region.Exclude($n)
        }
    }
    $region.Transform($m)

    # Light gradient from the base (bottom-left) to the tip (top-right).
    $fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush (PtF ($s * 0.2) ($s * 0.8)), (PtF ($s * 0.8) ($s * 0.2)), $c.Base, $c.Tip
    $g.FillRegion($fill, $region)

    # Barbs: faint diagonal strokes inside the vane, only where there is room to see them.
    if ($s -ge 48) {
        $g.SetClip($region, [System.Drawing.Drawing2D.CombineMode]::Replace)
        $barbPen = New-Object System.Drawing.Pen (Rgb $c.Shaft.R $c.Shaft.G $c.Shaft.B 70), ([Math]::Max(1, $s / 96))
        for ($x = 0.22; $x -le 0.92; $x += 0.08) {
            foreach ($side in -1, 1) {
                $barb = New-Object System.Drawing.Drawing2D.GraphicsPath
                $barb.AddLine((PtF $x 0), (PtF ($x + 0.10) (0.22 * $side)))
                $barb.Transform($m)
                $g.DrawPath($barbPen, $barb)
            }
        }
        $g.ResetClip()
    }

    # Shaft: from the quill (sticking out past the vane) almost to the tip.
    $shaft = New-Object System.Drawing.Drawing2D.GraphicsPath
    $shaft.AddBezier((PtF -0.10 0.015), (PtF 0.30 0.005), (PtF 0.70 -0.005), (PtF 0.95 0))
    $shaft.Transform($m)
    $shaftPen = New-Object System.Drawing.Pen $c.Shaft, ([Math]::Max(1.0, $s * 0.035))
    $shaftPen.StartCap = 'Round'; $shaftPen.EndCap = 'Round'
    if (-not $small) { $g.DrawPath($shaftPen, $shaft) }
    else {
        # At tiny sizes a dark shaft just muddies the shape; draw only the quill in the light color.
        $quill = New-Object System.Drawing.Drawing2D.GraphicsPath
        $quill.AddLine((PtF -0.10 0.015), (PtF 0.14 0.0))
        $quill.Transform($m)
        $g.DrawPath((New-Object System.Drawing.Pen $c.Base, ([Math]::Max(1.0, $s * 0.07))), $quill)
    }

    $g.Dispose(); $bmp
}

# ---------------------------------------------------------------- document icon (unchanged design)

function Draw-Page($g, [single]$x, [single]$y, [single]$w, [single]$h, [single]$foldSize, $outline) {
    $page = New-Object System.Drawing.Drawing2D.GraphicsPath
    $page.AddLines([System.Drawing.PointF[]]@((PtF $x $y), (PtF ($x + $w - $foldSize) $y), (PtF ($x + $w) ($y + $foldSize)),
        (PtF ($x + $w) ($y + $h)), (PtF $x ($y + $h))))
    $page.CloseFigure()
    $g.FillPath([System.Drawing.Brushes]::White, $page)
    if ($outline) { $g.DrawPath($outline, $page) }
    $corner = New-Object System.Drawing.Drawing2D.GraphicsPath
    $corner.AddLines([System.Drawing.PointF[]]@((PtF ($x + $w - $foldSize) $y), (PtF ($x + $w - $foldSize) ($y + $foldSize)),
        (PtF ($x + $w) ($y + $foldSize))))
    $corner.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush $fold), $corner)
}

function Draw-DocumentIcon([int]$s) {
    $bmp, $g = New-Canvas $s
    $px = $s * 0.16; $py = $s * 0.05; $pw = $s * 0.68; $ph = $s * 0.9
    $outline = New-Object System.Drawing.Pen $edge, ([Math]::Max(1, $s / 32))
    Draw-Page $g $px $py $pw $ph ($s * 0.2) $outline
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

# ---------------------------------------------------------------- .ico writer

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

# ---------------------------------------------------------------- preview / output

if ($Preview) {
    # Each variant at real sizes on a dark (taskbar-like) and a light background.
    $previewSizes = 16, 24, 32, 48, 64, 128, 256
    $rowW = ($previewSizes | Measure-Object -Sum).Sum + 14 * ($previewSizes.Count + 1)
    $rowH = 256 + 28
    $names = 'night', 'ember', 'teal'
    $sheet = [System.Drawing.Bitmap]::new([int]($rowW + 150), [int]($rowH * 2 * $names.Count))
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $font = New-Object System.Drawing.Font 'Segoe UI', 16, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $y = 0
    foreach ($name in $names) {
        foreach ($backdrop in (Rgb 32 32 32), (Rgb 243 243 243)) {
            $g.FillRectangle((New-Object System.Drawing.SolidBrush $backdrop), 0, $y, $sheet.Width, $rowH)
            $label = if ($backdrop.R -lt 100) { [System.Drawing.Brushes]::White } else { [System.Drawing.Brushes]::Black }
            $g.DrawString($name, $font, $label, 12, $y + $rowH / 2 - 10)
            $x = 150
            foreach ($s in $previewSizes) {
                $icon = Draw-AppIcon $s $name
                $g.DrawImage($icon, $x, $y + ($rowH - $s) / 2, $s, $s)
                $icon.Dispose()
                $x += $s + 14
            }
            $y += $rowH
        }
    }
    $g.Dispose()
    $dist = Join-Path $root 'dist'
    New-Item -ItemType Directory -Force $dist | Out-Null
    $out = Join-Path $dist 'icon-options.png'
    $sheet.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "preview: $out"
    return
}

Write-Ico (Join-Path $assets 'app.ico') { param($s) Draw-AppIcon $s $Variant }
Write-Ico (Join-Path $assets 'pdf-document.ico') ${function:Draw-DocumentIcon}
