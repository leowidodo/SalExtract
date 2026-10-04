namespace SalExtract.Infrastructure;

public sealed class ExtractionOptions
{
    public const string SectionName = "Extraction";
    public int NativeTextMinimumCharactersPerPage { get; set; } = 40;
    public int OcrDpi { get; set; } = 300;
    public string OcrLanguages { get; set; } = "ind+eng";
    public string TessDataPath { get; set; } = "tools/tessdata";
    public int MaxPages { get; set; } = 50;
}
