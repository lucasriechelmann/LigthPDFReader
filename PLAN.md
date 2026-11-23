# LightPDFReader — Plan

A fast, minimal PDF reader for Windows. No accounts, no cloud, no upsell pop-ups, no telemetry.

## Goals

- **Opens instantly.** Target: under 300 ms cold start to the first visible page on a typical file.
- **Small.** A single self-contained exe of about 30–40 MB (including `pdfium.dll`). No prerequisites.
- **Does only what's needed:** view, scroll, zoom, tabs, search, copy text, print, and open on double-click.

## Non-goals (deliberately left out)

Annotations, form filling, editing and saving PDFs, signatures, cloud sync, an auto-updater, accounts, telemetry.

## Technology stack

| Concern | Choice | Why |
|---|---|---|
| Language/runtime | C# on **.NET 10 (LTS)** | Familiar language; Native AOT is mature in .NET 10 |
| UI | **Avalonia** (latest stable) | WPF-like XAML, and unlike WPF it supports Native AOT |
| PDF engine | **PDFium** (`pdfium.dll` from `bblanchon/pdfium-binaries`, win-x64) | Chrome's engine, BSD-licensed, renders accurately, has a text API |
| Interop | Hand-written `[LibraryImport]` bindings | Source-generated P/Invoke works with AOT and has no reflection |
| Printing | Win32 GDI (`PrintDlgEx` + `StartDoc`) + `FPDF_RenderPage(HDC)` | Avalonia has no printing API; PDFium can draw directly to a printer DC |
| Publish | `PublishAot=true`, `win-x64`, trimmed, with `InvariantGlobalization` | Fastest startup, one exe |

## Solution layout

```
LightPDFReader.sln
src/
  LightPdf.Pdfium/      # Thin interop: native imports, SafeHandles, error mapping
  LightPdf.Core/        # PdfDocument, PdfPage, rendering, text/search, page cache. No UI types.
  LightPdf.App/         # Avalonia app: windows, tabs, viewer control, printing, single-instance
tests/
  LightPdf.Core.Tests/  # xUnit tests against sample PDFs
native/
  pdfium/               # pinned pdfium.dll + LICENSE (copied to output)
```

Keeping `Core` free of UI types makes the engine testable, and lets us swap UI frameworks later if needed.

## Key design decisions

1. **One dedicated render thread.** PDFium is *not* thread-safe. Every PDFium call goes through a single-threaded work queue (`Channel<T>` + one long-running thread). The UI thread never blocks on rendering.
2. **Virtualized page list.** Only render pages that are in or near the viewport. Use an LRU cache of rendered bitmaps capped by memory (for example 256 MB), not by page count.
3. **Zoom strategy.** On zoom, immediately show the cached bitmap scaled, which looks blurry for a moment. After a short pause (~150 ms) re-render at the new scale. Render the page in the center of the view first.
4. **First paint fast.** Open the file, render page 1 at screen resolution, show the window, then lay out the remaining pages. Page sizes come from `FPDF_GetPageSizeByIndex`, which doesn't need a render.
5. **Text features via `FPDFText_*`.** Search uses `FPDFText_FindStart/FindNext`, and highlights come from `FPDFText_GetRect`. Selection maps mouse position to a character with `FPDFText_GetCharIndexAtPos`, then copies with `FPDFText_GetText`.
6. **Single instance.** A named mutex detects a running instance. A named pipe forwards file paths to it, which opens them as new tabs.
7. **File association, per user, no admin.** Register a ProgID under `HKCU\Software\Classes` plus `RegisteredApplications`. Windows 11 won't let apps *set themselves* as default (`UserChoice` is hash-protected), so we register the app and open the "Default apps" settings page for the user to confirm.

## Milestones

### M0: Setup and spike (prove the risky bits)
- Create the solution, Avalonia app, and pinned `pdfium.dll`
- Minimal bindings: `FPDF_InitLibrary`, `LoadDocument`, `GetPageCount`, `LoadPage`, `RenderPageBitmap`
- Render page 1 into an Avalonia `WriteableBitmap`
- **Publish with Native AOT and confirm it runs.** Measure startup time and exe size.

**M0 results (2026-11-23), done:**
- Stack: .NET SDK 10.0.404 (pinned in `global.json`), Avalonia 12.1.6, PDFium `chromium/8165` (non-V8 build)
- AOT publish: **0 trim/AOT warnings**. Ships about 42 MB (exe 16.7 + Skia 11.1 + PDFium 7.5 + ANGLE 5.1 + HarfBuzz 1.7)
- Startup to first visible page: **~220 ms AOT** vs ~680 ms JIT. Page render: 6–16 ms
- Memory: ~100 MB working set (mostly Skia/GPU). Revisit in M6, possibly with software rendering to drop ANGLE
- Gotcha: the AOT link needs `vswhere.exe` on PATH, which `tools/Publish.ps1` handles
- Debug helpers: `LightPdf.exe --bench [--snapshot out.png] file.pdf`

### M1: Core viewer
- Continuous vertical scroll with virtualization, page gaps, and page shadows
- Zoom (Ctrl+wheel, Ctrl +/-, fit width, fit page), with the debounced re-render
- Render thread + LRU bitmap cache
- Page number box with "go to page", and Home/End/PgUp/PgDn keys
- Password-protected PDFs: prompt and retry

**M1 results (2026-11-23), done:**
- `PdfWorker` (single PDFium thread, cancellable FIFO), `PageLayout` (O(log n) visibility), `Zoom` (steps, fit math), all unit-tested (37 tests, including RC4-encrypted PDFs generated in code)
- `PdfPagesView`: virtualized continuous scroll, renders visible pages ±1 nearest-first, stretch-then-sharpen zoom (150 ms settle), 96 MB LRU cache
- Shortcuts: Ctrl+wheel / Ctrl +/− zoom (anchored), Ctrl+0 fit page, Ctrl+1 100%, Ctrl+2 fit width, Ctrl+G go to page, Home/End, PgUp/PgDn/Space, ←/→ page
- Password prompt with retry
- AOT: ~265 ms to first page; 42.4 MB; memory ~115 MB at open, ~245 MB after paging 120 times through a 383-page manual
- Bugs found in real-window testing: bitmaps tagged with DPI ≠ 96 were drawn magnified by the display scaling (fixed: bitmaps are 96 DPI and the dest rect sets the size); `PointerWheelChanged` is bubble-only (handled in the view itself); `ScrollViewer` isn't focusable by default
- Follow-ups: tiled rendering above ~16 MP per page (very high zoom is soft); profile the ~130 MB of non-cache growth (GPU textures?) in M6; Ctrl+wheel not yet verified by hand

### M2: Tabs and file handling
- Tabs (Ctrl+T/W, Ctrl+Tab, middle-click to close)
- Drag-and-drop files onto the window, Ctrl+O
- Recent files list (small JSON file in `%AppData%`)
- Remember the last page and zoom per file

**M2 results (2026-11-23), done:**
- Tabs (one shared PDFium worker): Ctrl+O/T open (multi-select), Ctrl+W/F4 close, middle-click close, Ctrl+Tab / Ctrl+Shift+Tab / Ctrl+PgUp/PgDn cycle; opening an already-open file switches to its tab
- Multiple files from the command line and drag-and-drop
- `RecentFilesStore` (Core, 8 tests): 20 most recent files with page, zoom and mode, in `%AppData%\LightPdf\recent.json`; source-generated JSON (AOT-safe), atomic save, tolerant of corrupt files; unreadable files are dropped from the list
- Recent ▾ menu, and an empty-state screen listing recent files
- Hidden tabs keep their layout and scroll position but free their bitmaps (2 tabs ≈ 130 MB, about the same as 1)
- AOT: ~258 ms to first page; 43.1 MB; 45 tests
- Bugs found in real-window testing: restoring a saved page from inside the first layout pass was clamped to page 1 (now deferred until after layout, and kept pending if the tab is hidden first); an empty `MenuFlyout` never opens, so `Opening` can't populate it (now prebuilt); rebuilding the menu while it was open left it stuck open
- Not exercised by hand: drag-and-drop from Explorer and multi-select in the Open dialog (same code path as the tested multi-file command line)

### M3: Search and copy
- Ctrl+F search bar with next/previous and highlighting all matches on visible pages
- Text selection with the mouse, Ctrl+C, Ctrl+A on the page

### M4: Printing
- Win32 print dialog (printer, page range, copies)
- Render each page to the printer HDC at printer DPI with `FPDF_RenderPage`, fit to paper

### M5: Windows integration
- Single instance + named-pipe forwarding
- Command line: `LightPdf.exe file.pdf [file2.pdf]`
- Per-user file association + "Set as default" button that opens Settings
- App icon and file icon

### M6: Polish and release
- Dark mode (follow the system theme; optional inverted page colors)
- Error handling for corrupt or unsupported files
- Startup and memory profiling against the targets above
- Portable zip, plus an optional tiny installer (Inno Setup)

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| AOT trim warnings from Avalonia or our code | Treat trim/AOT warnings as errors from M0; use compiled bindings (`x:CompileBindings`) |
| PDFium crashes on malformed files bring down the app | Catch and report at the render-thread boundary; test with a corpus of bad PDFs |
| Memory use on huge documents | Memory-capped cache, render only visible pages, release `FPDF_PAGE` handles promptly |
| Printing quality and speed | Use the vector path (`FPDF_RenderPage` to HDC) rather than printing bitmaps |

## Licensing

PDFium is BSD-3 and Avalonia is MIT. Ship their license texts in a `THIRD-PARTY-NOTICES.txt`.
