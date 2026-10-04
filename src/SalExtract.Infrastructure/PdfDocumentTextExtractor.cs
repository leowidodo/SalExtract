using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PDFtoImage;
using SkiaSharp;
using Tesseract;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using SalExtract.Application;

namespace SalExtract.Infrastructure;

public sealed class PdfDocumentTextExtractor(IOptions<ExtractionOptions> options, ILogger<PdfDocumentTextExtractor> logger) : IDocumentTextExtractor
{
    private readonly ExtractionOptions _options = options.Value;

    public async Task<DocumentContent> ExtractAsync(Stream pdf, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await pdf.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        var pages = new List<DocumentPage>();
        var warnings = new List<string>();
        using (var document = PdfDocument.Open(bytes))
        {
            if (document.NumberOfPages > _options.MaxPages) throw new InvalidDataException($"PDF exceeds the {_options.MaxPages}-page limit.");
            foreach (var page in document.GetPages())
                pages.Add(new DocumentPage(page.Number, BuildReadingOrderText(page), "native-pdf"));
        }

        var needsOcr = pages.Where(x => x.Text.Count(char.IsLetterOrDigit) < _options.NativeTextMinimumCharactersPerPage).Select(x => x.PageNumber).ToHashSet();
        if (needsOcr.Count == 0) return new DocumentContent(pages, "native-pdf", warnings);
        var tessData = Path.GetFullPath(_options.TessDataPath, AppContext.BaseDirectory);
        if (!Directory.Exists(tessData))
        {
            warnings.Add($"OCR required for {needsOcr.Count} page(s), but tessdata was not found at '{tessData}'. Run setup.ps1.");
            return new DocumentContent(pages, pages.Any(x => !string.IsNullOrWhiteSpace(x.Text)) ? "native-pdf-partial" : "ocr-unavailable", warnings);
        }

        try
        {
            using var engine = new TesseractEngine(tessData, _options.OcrLanguages, EngineMode.LstmOnly);
            foreach (var pageNumber in needsOcr)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Phase 1 OCR rendering targets Windows.");
                await using var source = new MemoryStream(bytes);
                using var bitmap = Conversion.ToImage(source, page: pageNumber - 1, options: new RenderOptions(Dpi: _options.OcrDpi));
                var text = Recognize(engine, bitmap);
                using var rightHalf = new SKBitmap();
                var rightHalfBounds = SKRectI.Create(bitmap.Width / 2, 0, bitmap.Width - (bitmap.Width / 2), bitmap.Height);
                if (bitmap.ExtractSubset(rightHalf, rightHalfBounds))
                {
                    // Tables can cause Tesseract's whole-page layout analysis to omit the
                    // rightmost total column. A focused pass retains those values and labels.
                    var rightHalfText = Recognize(engine, rightHalf);
                    if (!string.IsNullOrWhiteSpace(rightHalfText)) text = $"{text.TrimEnd()}\n{rightHalfText}";
                }
                var index = pages.FindIndex(x => x.PageNumber == pageNumber);
                pages[index] = new DocumentPage(pageNumber, text, "ocr-tesseract");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "OCR processing failed");
            warnings.Add("OCR processing failed. See server logs for diagnostic details.");
        }
        var method = pages.All(x => x.Method == "ocr-tesseract") ? "ocr-tesseract" : pages.Any(x => x.Method == "ocr-tesseract") ? "hybrid" : "native-pdf-partial";
        return new DocumentContent(pages, method, warnings);
    }

    private static string Recognize(TesseractEngine engine, SKBitmap bitmap)
    {
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var pix = Pix.LoadFromMemory(encoded.ToArray());
        using var page = engine.Process(pix, PageSegMode.Auto);
        return page.GetText() ?? string.Empty;
    }

    private static string BuildReadingOrderText(UglyToad.PdfPig.Content.Page page)
    {
        var words = page.GetWords().OrderByDescending(x => x.BoundingBox.Bottom).ThenBy(x => x.BoundingBox.Left).ToList();
        if (words.Count == 0) return page.Text ?? string.Empty;
        var lines = new List<List<Word>>();
        foreach (var word in words)
        {
            var line = lines.LastOrDefault();
            if (line is null || Math.Abs(line.Average(x => x.BoundingBox.Bottom) - word.BoundingBox.Bottom) > Math.Max(2d, word.BoundingBox.Height * .55d))
                lines.Add([word]);
            else
                line.Add(word);
        }
        return string.Join(Environment.NewLine, lines.Select(line => string.Join(' ', line.OrderBy(x => x.BoundingBox.Left).Select(x => x.Text))));
    }
}
