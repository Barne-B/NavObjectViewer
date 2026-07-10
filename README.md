# NAV Object Viewer & Exporter

A Prism-for-C/AL–style desktop tool for **browsing, filtering, searching, and
re-exporting** objects from classic Dynamics NAV / C/SIDE text exports — including
very large files (the reference export is 243 MB / 7,643 objects). Unlike Prism, it
**exports the objects you select**, either as one combined file or as many small
per-object files that re-import into C/SIDE byte-for-byte.

## Highlights

- **Handles huge exports** — the file is indexed by byte offset in a single streaming
  pass (243 MB in ~0.6 s); object text is read on demand, never all at once.
- **CP850-aware** — classic C/SIDE exports are OEM codepage 850, not UTF-8. Text is
  decoded as CP850 for display; exports copy the **original bytes verbatim** for a
  guaranteed round-trip.
- **Filter** by object type, ID range (e.g. custom range ≥ 50000), name (substring or
  `*`/`?` wildcards), version list, and the Modified flag.
- **View** a selected object with C/AL syntax highlighting (read-only).
- **Full-text search** inside object bodies (case-insensitive / regex), scoped to
  all / the current filter / the custom range. Results are **uncapped and summarized
  per object** (a tree of "which objects match, and how many times"); expand an object
  to load its matching lines on demand and double-click a line to jump to it.
- **Content-based grid filter** — "Filter list to matches" narrows the object list on
  the left to just the objects a search found, so you can then select/export them.
- **Export** ticked objects as a single combined `.txt`, or one `<Prefix><Id>.txt`
  per object (`COD80040.txt`, `TAB50000.txt`, …) grouped into per-type subfolders.
- Robust to the real data's quirks, e.g. the bracketed header
  `OBJECT [Report 50014 … ]` (flagged with ⚠ in the grid).

## Project layout

```
src/NavObjectViewer.Core/   parser + exporter + search (no UI, unit-tested)
  Models/                   NavObject, NavObjectType
  Services/                 ObjectIndexer, ObjectReader, ObjectExporter,
                            FullTextSearch, IndexCache, Encodings (CP850)
src/NavObjectViewer/        WPF app (MVVM), AvalonEdit viewer
tests/NavObjectViewer.Tests xUnit tests (indexer, exporter round-trip, search, cache)
```

## Requirements

- Windows, .NET 8 SDK (to build). The published single-file exe is self-contained
  and needs no runtime installed.

## Run

Prebuilt self-contained executable:

```
src\NavObjectViewer\bin\Release\net8.0-windows\win-x64\publish\NavObjectViewer.exe
```

Then **Open File…** (your combined `.txt`) or **Open Folder…** (a folder of `.txt`
exports), filter/search, tick objects, and **Export selected…**.

You can also open a file directly:

```
NavObjectViewer.exe "path\to\export.txt"
```

## Build / test / package

```powershell
dotnet build  NavObjectViewer.sln -c Release
dotnet test   tests/NavObjectViewer.Tests
dotnet publish src/NavObjectViewer/NavObjectViewer.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

## Notes

- The first index of a file is cached under
  `%LOCALAPPDATA%\NavObjectViewer\cache`, keyed by size + last-write time, so
  re-opening an unchanged file is instant.
- The source file is opened read-only and never modified.
- Very large objects (e.g. a Report's multi-MB RDLDATA base64) are shown without
  syntax highlighting and with word-wrap on, so the viewer never hangs on them.
- Any unexpected error is logged to `%LOCALAPPDATA%\NavObjectViewer\logs\error.log`
  and shown in a dialog rather than crashing the app.
- Not yet implemented (possible next phase): cross-references / find-usages / table
  relationship navigation, and a UTF-8 re-encode export option.
