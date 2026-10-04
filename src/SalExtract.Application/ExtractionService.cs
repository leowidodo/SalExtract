using System.Diagnostics;
using System.Security.Cryptography;
using SalExtract.Domain;

namespace SalExtract.Application;

public sealed class ExtractionService(IDocumentTextExtractor documentExtractor, IInvoiceFieldExtractor fieldExtractor, IExtractionRepository repository) : IExtractionService
{
    public async Task<ExtractionResult> ExtractAsync(Stream pdf, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        await using var copy = new MemoryStream();
        await pdf.CopyToAsync(copy, cancellationToken);
        if (copy.Length < 5 || !copy.GetBuffer().AsSpan(0, 5).SequenceEqual("%PDF-"u8)) throw new InvalidDataException("File is not a valid PDF.");
        var hash = Convert.ToHexString(SHA256.HashData(copy.GetBuffer().AsSpan(0, checked((int)copy.Length)))).ToLowerInvariant();
        copy.Position = 0;
        var timer = Stopwatch.StartNew();
        var document = await documentExtractor.ExtractAsync(copy, cancellationToken);
        var fields = fieldExtractor.Extract(document);
        timer.Stop();
        var required = new[] { "SupplierName", "InvoiceNo", "InvoiceDate", "TotalInvoice", "Currency" };
        var missing = required.Where(x => !fields.TryGetValue(x, out var f) || string.IsNullOrWhiteSpace(f.NormalizedValue)).ToArray();
        var lowConfidence = fields.Values.Any(x => x.Confidence is > 0 and < .75m);
        var warnings = document.Warnings.Concat(missing.Select(x => $"Required field '{x}' was not extracted.")).ToArray();
        var result = new ExtractionResult { FileName = Path.GetFileName(fileName), Sha256 = hash, SourceMethod = document.Method, PageCount = document.Pages.Count, ProcessingMilliseconds = timer.ElapsedMilliseconds, Fields = fields, Warnings = warnings, Status = missing.Length > 0 || lowConfidence ? ExtractionStatus.Review : ExtractionStatus.Completed };
        await repository.SaveAsync(result, cancellationToken);
        return result;
    }
}
