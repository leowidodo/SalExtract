namespace SalExtract.Domain;

public enum ExtractionStatus { Completed, Review, Failed }

public sealed record ExtractedField(
    string Name,
    string? RawValue,
    string? NormalizedValue,
    decimal Confidence,
    string Method,
    int? Page,
    string? Evidence,
    IReadOnlyList<string>? Warnings = null);

public sealed class ExtractionResult
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string FileName { get; init; }
    public required string Sha256 { get; init; }
    public DateTimeOffset ProcessedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public ExtractionStatus Status { get; init; }
    public required string SourceMethod { get; init; }
    public int PageCount { get; init; }
    public long ProcessingMilliseconds { get; init; }
    public required IReadOnlyDictionary<string, ExtractedField> Fields { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

