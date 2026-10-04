using SalExtract.Application;

namespace SalExtract.UnitTests;

public sealed class InvoiceFieldExtractorTests
{
    private readonly InvoiceFieldExtractor _sut = new();

    [Fact]
    public void Extract_Normalizes_Indonesian_Invoice()
    {
        var document = new DocumentContent([new DocumentPage(1, "PT Teknologi Cemerlang\nInvoice No: INV-2026/001\nTanggal Invoice: 3 Oktober 2026\nGrand Total: Rp 1.234.567,89", "native-pdf")], "native-pdf", []);
        var result = _sut.Extract(document);
        Assert.Equal("PT Teknologi Cemerlang", result["SupplierName"].NormalizedValue);
        Assert.Equal("INV-2026/001", result["InvoiceNo"].NormalizedValue);
        Assert.Equal("2026-10-03", result["InvoiceDate"].NormalizedValue);
        Assert.Equal("1234567.89", result["TotalInvoice"].NormalizedValue);
        Assert.Equal("IDR", result["Currency"].NormalizedValue);
        Assert.Equal("Rp", result["Currency"].RawValue);
    }

    [Theory]
    [InlineData("Invoice Date: 31/12/2025", "2025-12-31")]
    [InlineData("Date: 2026-01-15", "2026-01-15")]
    public void Extract_Normalizes_Date_Formats(string line, string expected)
    {
        var result = _sut.Extract(new DocumentContent([new DocumentPage(1, line, "native-pdf")], "native-pdf", []));
        Assert.Equal(expected, result["InvoiceDate"].NormalizedValue);
    }

    [Fact]
    public void Extract_Missing_Fields_Are_Auditable()
    {
        var result = _sut.Extract(new DocumentContent([new DocumentPage(1, "unrelated", "native-pdf")], "native-pdf", []));
        Assert.Null(result["TotalInvoice"].NormalizedValue);
        Assert.Contains("Field not found", result["TotalInvoice"].Warnings!);
        Assert.Equal("IDR", result["Currency"].NormalizedValue);
        Assert.Equal("default", result["Currency"].Method);
    }

    [Fact]
    public void Extract_Recognizes_Generic_Number_And_Table_Total_From_Ocr()
    {
        var text = """
            BENGKEL MOTOR ABADI
            NOTA BENGKEL
            No: 102/BMA/26 Tgl: 1 Okt 2026 Nopol: B 3452 TQ
            TOTAL: | 160.000
            """;

        var result = _sut.Extract(new DocumentContent([new DocumentPage(1, text, "ocr-tesseract")], "ocr-tesseract", []));

        Assert.Equal("102/BMA/26", result["InvoiceNo"].NormalizedValue);
        Assert.Equal("2026-10-01", result["InvoiceDate"].NormalizedValue);
        Assert.Equal(.92m, result["InvoiceDate"].Confidence);
        Assert.Equal("160000.00", result["TotalInvoice"].NormalizedValue);
    }

    [Fact]
    public void Extract_Recognizes_Total_Value_Stacked_Below_Label()
    {
        var text = """
            Budi Santoso
            Description Rate Hours Total
            Wireframing Mobile App $25/hr 10 $250.00
            Total Due
            $1,025.00
            Payment Information:
            """;

        var result = _sut.Extract(new DocumentContent([new DocumentPage(1, text, "native-pdf")], "native-pdf", []));

        Assert.Equal("1025.00", result["TotalInvoice"].NormalizedValue);
        Assert.Equal("Total Due -> $1,025.00", result["TotalInvoice"].Evidence);
        Assert.Contains(result["TotalInvoice"].Warnings!, warning => warning.Contains("stacked label/value layout", StringComparison.Ordinal));
        Assert.Equal("USD", result["Currency"].NormalizedValue);
        Assert.Equal("$", result["Currency"].RawValue);
    }

    [Fact]
    public void Extract_Does_Not_Treat_Total_Column_Header_As_Stacked_Invoice_Total()
    {
        var text = "Description Rate Hours Total\nWireframing Mobile App $25/hr 10 $250.00";

        var result = _sut.Extract(new DocumentContent([new DocumentPage(1, text, "native-pdf")], "native-pdf", []));

        Assert.Null(result["TotalInvoice"].NormalizedValue);
    }

    [Fact]
    public void Extract_Dollar_Marker_Overrides_Rupiah_Default_And_Marker()
    {
        var text = "Grand Total: $10.00\nReference amount: Rp 100.000";

        var result = _sut.Extract(new DocumentContent([new DocumentPage(1, text, "native-pdf")], "native-pdf", []));

        Assert.Equal("USD", result["Currency"].NormalizedValue);
        Assert.Equal(.99m, result["Currency"].Confidence);
    }
}
