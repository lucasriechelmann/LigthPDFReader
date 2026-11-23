<#
  Builds the release: Native AOT publish, LICENSE + THIRD-PARTY-NOTICES.txt next to the exe,
  a portable zip, and (if Inno Setup 6 is installed) a per-user installer. Output goes to ./dist.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'No <Version> in Directory.Build.props' }

& (Join-Path $PSScriptRoot 'Publish.ps1')
$publish = Join-Path $root 'publish'

# --- licenses -----------------------------------------------------------------------------------------
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $publish 'LICENSE.txt') -Force

$pdfiumLicenses = Join-Path $root 'native/pdfium/win-x64/licenses'
$licenses = Join-Path $root 'licenses'
$sections = @(
    @{ Title = 'PDFium (pdfium.dll)';                          File = Join-Path $pdfiumLicenses 'pdfium.txt' }
    @{ Title = 'pdfium-binaries build scripts';                File = Join-Path $pdfiumLicenses 'pdfium-binaries-build-scripts.txt' }
    @{ Title = 'Avalonia UI';                                  File = Join-Path $licenses 'avalonia.txt' }
    @{ Title = 'SkiaSharp (libSkiaSharp.dll)';                 File = Join-Path $licenses 'skiasharp.txt' }
    @{ Title = 'HarfBuzzSharp (libHarfBuzzSharp.dll)';         File = Join-Path $licenses 'harfbuzzsharp.txt' }
    @{ Title = 'ANGLE (av_libglesv2.dll)';                     File = Join-Path $licenses 'angle.txt' }
    @{ Title = '.NET runtime (compiled into LightPdf.exe)';    File = Join-Path $licenses 'dotnet.txt' }
)
# Libraries bundled inside pdfium.dll (FreeType, libjpeg-turbo, zlib, ...).
foreach ($f in Get-ChildItem $pdfiumLicenses -File | Where-Object { $_.Name -notin 'pdfium.txt', 'pdfium-binaries-build-scripts.txt' } | Sort-Object Name) {
    $sections += @{ Title = "$($f.BaseName) (bundled in pdfium.dll)"; File = $f.FullName }
}
$sections += @{ Title = 'Third-party notices for SkiaSharp / HarfBuzzSharp native libraries'; File = Join-Path $licenses 'skiasharp-third-party-notices.txt' }
$sections += @{ Title = 'Third-party notices for the .NET runtime';                       File = Join-Path $licenses 'dotnet-third-party-notices.txt' }

$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine("LightPdf $version includes the following third-party software.")
[void]$sb.AppendLine('LightPdf itself is licensed under the MIT License (LICENSE.txt).')
foreach ($s in $sections) {
    if (-not (Test-Path $s.File)) { throw "Missing license file: $($s.File)" }
    [void]$sb.AppendLine().AppendLine('=' * 78).AppendLine($s.Title).AppendLine('=' * 78).AppendLine()
    [void]$sb.AppendLine((Get-Content $s.File -Raw).TrimEnd())
}
[System.IO.File]::WriteAllText((Join-Path $publish 'THIRD-PARTY-NOTICES.txt'), $sb.ToString())

# --- portable zip --------------------------------------------------------------------------------------
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$zip = Join-Path $dist "LightPdf-$version-win-x64-portable.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal

# --- installer -----------------------------------------------------------------------------------------
$iscc = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($iscc) {
    & $iscc /Q "/DAppVersion=$version" (Join-Path $root 'installer\LightPdf.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed ($LASTEXITCODE)" }
} else {
    Write-Warning 'Inno Setup 6 not found: skipped the installer (portable zip only).'
}

Get-ChildItem $dist -File | Format-Table Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
