# Visual Studio setup

## Prerequisites

- Windows 11 x64.
- Visual Studio with **ASP.NET and web development** workload and .NET 10 SDK.
- Internet access once during setup for NuGet packages and Apache-2.0 Tesseract language data. Runtime processing is offline.

## First setup

Open PowerShell in the repository and run `./setup.ps1`. The script checks .NET, downloads `eng` and `ind` OCR models, restores packages, builds, and runs all tests. Use `-SkipOcrModels` only when testing native PDFs without OCR.

## Open, build and run

1. Open `SalExtract.sln`.
2. Visual Studio reads `SalExtract.slnLaunch`; select the `SalExtract` multi-project launch profile if prompted.
3. Use **Build > Build Solution** (`Ctrl+Shift+B`), **Clean Solution**, or **Rebuild Solution** normally.
4. Press F5. `SalExtract.Api` starts on port 5080 and `SalExtract.Web` opens in the browser.
5. Select a PDF, click **Extract invoice**, and inspect normalized values, confidence, and evidence.

Set breakpoints in the Web page model, API endpoint, `ExtractionService`, `PdfDocumentTextExtractor`, or `InvoiceFieldExtractor`. Application and EF logs appear in Visual Studio's Debug/ASP.NET output.

## Tests

Use **Test > Test Explorer > Run All**, or run `dotnet test SalExtract.sln`. Unit tests cover deterministic extraction/normalization; integration tests start the API in memory and exercise HTTP behavior.

## OCR worker

Phase 1 deliberately runs OCR in-process, so no separate worker or terminal is required. Low-text pages automatically use local PDFium rendering and Tesseract. A queued worker can later implement the existing `IDocumentTextExtractor` boundary without changing clients.

## Troubleshooting

- **API unavailable in Web:** ensure both Api and Web projects are selected in the `.slnLaunch` profile.
- **OCR unavailable:** rerun `./setup.ps1` and confirm `tools/tessdata/eng.traineddata` and `ind.traineddata` exist.
- **Native DLL load failure:** build/run as x64 and install the current Microsoft Visual C++ Redistributable.
- **HTTPS certificate warning:** development uses HTTP ports intentionally; production TLS belongs on the internal reverse proxy.
- **Database locked:** stop duplicate API processes; development data is under the API output `data` directory.
- **Restore failure:** verify access to `https://api.nuget.org/v3/index.json` and rerun setup.
