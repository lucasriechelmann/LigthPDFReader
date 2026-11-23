# LightPdf

A fast, minimal PDF reader for Windows. It opens instantly, does what a reader should, and nothing else:
no accounts, no cloud, no telemetry, no pop-ups.

**Website and downloads:** https://lucasriechelmann.github.io/LigthPDFReader/

- **Fast:** about 0.3 s from launch to the first page (Native AOT, PDFium)
- **Read:** continuous scrolling, zoom (Ctrl+wheel, Ctrl +/−, fit width, fit page), tabs, resume where you left off
- **Find and copy:** Ctrl+F search across the whole document, select text with the mouse, Ctrl+C
- **Print:** the standard Windows print dialog, with sharp (vector) output
- **Comfort:** follows Windows light/dark mode; night reading inverts page colors (Ctrl+I)
- **Windows integration:** one window for all your PDFs, "Open with" and default-app registration, per user (no admin rights)

## Install

Download from the releases page, then either:

- **Installer** (`LightPdf-x.y.z-setup.exe`): installs for your user only, adds a Start menu entry and,
  optionally, registers LightPdf for PDF files. Choose LightPdf as the default in Windows Settings.
- **Portable** (`LightPdf-x.y.z-win-x64-portable.zip`): unzip anywhere and run `LightPdf.exe`.
  To use it for PDF files, open the ⋮ menu and choose *Make LightPdf the default PDF app…*.

## Keyboard shortcuts

| Keys | Action |
|---|---|
| Ctrl+O / Ctrl+T | Open (several files at once is fine) |
| Ctrl+W, middle-click a tab | Close tab |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous tab |
| Ctrl+F, Enter / Shift+Enter, F3 / Shift+F3 | Find, next / previous match |
| Ctrl+G | Go to page |
| Ctrl+wheel, Ctrl+ +/− | Zoom |
| Ctrl+0 / Ctrl+1 / Ctrl+2 | Fit page / 100% / fit width |
| Ctrl+A, Ctrl+C | Select page text, copy selection |
| Ctrl+P | Print |
| Ctrl+I | Invert page colors (night reading) |
| Home / End, PgUp / PgDn, Space | Navigate |

## Build

Requirements: Windows 10/11 x64, .NET 10 SDK, Visual Studio 2022+ (or Build Tools) with
*Desktop development with C++* (for the Native AOT linker). Inno Setup 6 is optional (installer).

```powershell
dotnet test                 # 90+ unit tests
./tools/Publish.ps1         # Native AOT build in ./publish
./tools/Package.ps1         # ./dist: portable zip + installer
```

Useful diagnostics: `LightPdf.exe --bench file.pdf` (startup time),
`--bench-scroll 120` (frame pacing, memory, CPU while paging), `--print-to-pdf out.pdf` (print path check).

## Architecture

See [PLAN.md](PLAN.md): goals, design decisions, and measured results for each milestone.

- `src/LightPdf.Pdfium`: source-generated P/Invoke bindings to PDFium
- `src/LightPdf.Core`: documents, layout, search, printing layout, settings, single-instance, file association (UI-free, unit-tested)
- `src/LightPdf.App`: Avalonia 12 UI

## License

MIT (see [LICENSE](LICENSE)). Third-party licenses are in `THIRD-PARTY-NOTICES.txt` next to the exe,
built from `licenses/` and `native/pdfium/win-x64/licenses/`.
