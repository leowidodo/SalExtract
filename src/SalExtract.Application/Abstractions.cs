using SalExtract.Domain;

namespace SalExtract.Application;

public sealed record DocumentPage(int PageNumber, string Text, string Method);
public sealed record DocumentContent(IReadOnlyList<DocumentPage> Pages, string Method, IReadOnlyList<string> Warnings);

public interface IDocumentTextExtractor
{
    Task<DocumentContent> ExtractAsync(Stream pdf, CancellationToken cancellationToken);
}

public interface IExtractionRepository
{
    Task SaveAsync(ExtractionResult result, CancellationToken cancellationToken);
    Task<ExtractionResult?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ExtractionResult>> ListAsync(int limit, CancellationToken cancellationToken);
}

public interface IInvoiceFieldExtractor
{
    IReadOnlyDictionary<string, ExtractedField> Extract(DocumentContent document);
}

public interface IExtractionService
{
    Task<ExtractionResult> ExtractAsync(Stream pdf, string fileName, CancellationToken cancellationToken = default);
}

