# Third-party dependency policy

Only free/open-source components suitable for internal commercial use are selected. Lock files and a formal SBOM/license scan should be added to the organization's release pipeline.

| Component | Use | License |
|---|---|---|
| .NET / ASP.NET Core / EF Core / SQLite provider | Runtime, HTTP, persistence | MIT |
| PdfPig | Native PDF text reading | Apache-2.0 |
| PDFtoImage | PDF rendering wrapper | MIT |
| PDFium | PDF rendering engine | BSD-style license |
| SkiaSharp / Skia | Image memory and encoding | MIT / BSD-style license |
| Tesseract .NET wrapper | OCR integration | Apache-2.0 |
| Tesseract OCR and tessdata_fast | Local OCR and language data | Apache-2.0 |
| xUnit / Microsoft test SDK | Automated tests | Apache-2.0 / MIT |

No GPL/AGPL dependency, paid SDK, cloud OCR, or hosted AI service is intentionally used by the application.
