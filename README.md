# SalExtract

SalExtract is an on-premise, deterministic PDF invoice extraction engine. Phase 1 accepts native or scanned PDFs, extracts and normalizes five initial fields, stores an auditable result in local SQLite, exposes a REST API, and provides a manual-test web UI.

## Quick start

```powershell
.\setup.ps1
```

Open `SalExtract.sln`, select the `SalExtract` multi-project launch profile, and press F5. The web UI calls the local API; no document data is sent to the internet.

## Phase 1 capabilities

- Native PDF text extraction with PdfPig.
- Per-page OCR fallback using PDFium rendering and Tesseract (`ind+eng`).
- Rule-based extraction for `SupplierName`, `InvoiceNo`, `InvoiceDate`, `TotalInvoice`, and `Currency` (`IDR` by default, `USD` when `$` is present).
- Canonical ISO date and invariant decimal normalization.
- Evidence, page, method, warning, and confidence for every field.
- SQLite audit store and `POST/GET /api/v1/extractions` API.
- 25 MB upload limit, 50-page limit, PDF signature validation, rate limiting, safe filenames, optional API key.

See [Visual Studio setup](docs/VISUAL_STUDIO_SETUP.md), [architecture](docs/ARCHITECTURE.md), and [API examples](docs/API.md).

## Scope boundary

Phase 1 does not implement UiPath orchestration, desktop-application comparison, Excel reports, or line-item/table extraction. The abstractions and versioned API are prepared for these future phases.
