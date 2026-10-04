# Architecture

SalExtract uses a Visual Studio-friendly modular monolith.

| Project | Responsibility |
|---|---|
| `SalExtract.Domain` | Stable result and audit models |
| `SalExtract.Application` | Extraction pipeline, field rules, normalization, ports |
| `SalExtract.Infrastructure` | PdfPig, PDFium/Tesseract, SQLite adapters |
| `SalExtract.Api` | Versioned HTTP boundary, validation and operational controls |
| `SalExtract.Web` | Human-operated upload and evidence viewer |
| `SalExtract.UnitTests` | Deterministic rules and normalization tests |
| `SalExtract.IntegrationTests` | In-process HTTP API tests |

## Processing flow

1. Validate extension, size, PDF magic bytes, and page limit.
2. Calculate SHA-256 for traceability.
3. Extract embedded text per page with PdfPig.
4. Render only low-text pages with PDFium and OCR them locally with Tesseract.
5. Apply explicit label aliases and regex rules.
6. Normalize dates to `yyyy-MM-dd`, monetary amounts to an invariant two-decimal string, and currency to `USD` when `$` is present or `IDR` by default.
7. Derive `Completed` or `Review` and persist the full result as JSON in SQLite.

Rules remain separate from document decoding, so supplier profiles and table extraction can be added without changing the API. No LLM, generative AI, cloud endpoint, or non-deterministic decision service is used.

## Production notes

Set `Security:ApiKey` through environment variable `Security__ApiKey`, terminate TLS at the internal reverse proxy, restrict network access to approved UiPath runners, place the SQLite data directory on encrypted and backed-up storage, and configure retention. For concurrent multi-node deployment, replace the repository adapter with an on-premise SQL Server or PostgreSQL implementation.
