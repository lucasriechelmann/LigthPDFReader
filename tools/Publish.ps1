<#
  Publishes LightPdf as a Native AOT app into ./publish (debug symbols removed).
  Requires Visual Studio (or Build Tools) with the "Desktop development with C++" workload.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# The AOT linker step locates MSVC via vswhere.exe, which is not on PATH by default.
$vsInstaller = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if (Test-Path $vsInstaller) { $env:PATH = "$vsInstaller;$env:PATH" }

$out = Join-Path $root 'publish'
Remove-Item -Recurse -Force $out -ErrorAction SilentlyContinue
dotnet publish (Join-Path $root 'src/LightPdf.App') -c Release -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

Remove-Item (Join-Path $out '*.pdb')
$total = (Get-ChildItem $out -File | Measure-Object Length -Sum).Sum / 1MB
Get-ChildItem $out -File | Sort-Object Length -Descending |
    Format-Table Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 2) } }
Write-Host ("Total: {0:N1} MB -> {1}" -f $total, $out)
