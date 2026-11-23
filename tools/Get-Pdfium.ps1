<#
  Downloads the pinned PDFium build (bblanchon/pdfium-binaries, no V8/XFA)
  into native/pdfium. Re-run after changing $Version.
#>
param(
    [string]$Version = 'chromium/8165',
    [string]$Rid = 'win-x64'
)
$ErrorActionPreference = 'Stop'

$root   = Split-Path -Parent $PSScriptRoot
$dest   = Join-Path $root "native/pdfium/$Rid"
$work   = Join-Path ([IO.Path]::GetTempPath()) ("pdfium-" + [Guid]::NewGuid())
$url    = "https://github.com/bblanchon/pdfium-binaries/releases/download/$Version/pdfium-$Rid.tgz"

New-Item -ItemType Directory -Force $work, $dest | Out-Null
try {
    $tgz = Join-Path $work 'pdfium.tgz'
    Write-Host "Downloading $url"
    Invoke-WebRequest $url -OutFile $tgz
    tar -xzf $tgz -C $work

    Copy-Item (Join-Path $work 'bin/pdfium.dll') $dest -Force
    # licenses/ holds PDFium's own BSD license and those of the libraries it bundles (FreeType, libjpeg-turbo,
    # zlib, ...). The top-level LICENSE is only the MIT license of the pdfium-binaries build scripts.
    $licenses = Join-Path $dest 'licenses'
    Remove-Item -Recurse -Force $licenses -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $work 'licenses') $licenses -Recurse -Force
    Copy-Item (Join-Path $work 'LICENSE') (Join-Path $licenses 'pdfium-binaries-build-scripts.txt') -Force
    Remove-Item (Join-Path $dest 'LICENSE.txt') -ErrorAction SilentlyContinue

    $hash = (Get-FileHash (Join-Path $dest 'pdfium.dll') -Algorithm SHA256).Hash
    "version=$Version`nrid=$Rid`nsha256=$hash" | Set-Content (Join-Path $dest 'VERSION.txt')
    Write-Host "pdfium.dll ($Version) -> $dest  sha256=$hash"
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
