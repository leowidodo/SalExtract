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

    [Fact]
    public void Extract_Recognizes_Top_Right_Generic_Number_Before_Invoice_In_Notes()
    {
        var text = "STUDIO KREATIF No. 088/SK/X\nTanggal: 02 Oktober 2026\n* Pembayaran jatuh tempo 14 hari setelah invoice diterima.";
        var fields = _sut.Extract(new DocumentContent([new DocumentPage(1, text, "native-pdf")], "native-pdf", []));
        Assert.Equal("088/SK/X", fields["InvoiceNo"].NormalizedValue);
    }

    [Theory]
    [InlineData("Invoice Date: unavailable\nTGL: 29-SEP-2026 KASIR: SITI", "2026-09-29")]
    [InlineData("Tanggal: 29 September 2026", "2026-09-29")]
    [InlineData("Tgl. 29-SEP-2026", "2026-09-29")]
    [InlineData("Tanggal: 29-Okt-2026", "2026-10-29")]
    [InlineData("Date: 01/09/2026\nInvoice Date: 29-SEP-2026", "2026-09-29")]
    [InlineData("Invoice Date\n29-SEP-2026", "2026-09-29")]
    [InlineData("Date:\n30.0ct.2025", "2025-10-30")]
    public void Extract_Tries_Valid_Date_Labels_In_Priority_Order(string text, string expected)
    {
        var fields = _sut.Extract(new DocumentContent([new DocumentPage(1, text, "ocr-tesseract")], "ocr-tesseract", []));
        Assert.Equal(expected, fields["InvoiceDate"].NormalizedValue);
    }

    [Fact]
    public void Extract_Skips_Invalid_Invoice_Label_And_Invalid_Dates()
    {
        var fields = _sut.Extract(new DocumentContent([new DocumentPage(1, "invoice diterima\nInvoice No: 088/SK/X\nTGL: 31-SEP-2026", "native-pdf")], "native-pdf", []));
        Assert.Equal("088/SK/X", fields["InvoiceNo"].NormalizedValue);
        Assert.Null(fields["InvoiceDate"].NormalizedValue);
    }

    [Theory]
    [InlineData("INVOICE TO: INVOICE #2026-015", "2026-015")]
    [InlineData("INVOICE NUMBER\n2510/AR/003878", "2510/AR/003878")]
    [InlineData("TAX INVOICE No: 04002500353069895\nInvoice No: MPX-X/HPM/X/25/001", "MPX-X/HPM/X/25/001")]
    [InlineData("No. INV/0020/25/ 10\nFAKTUR PENJUALAN", "INV/0020/25/10")]
    public void Extract_Recognizes_Additional_Sample_Number_Layouts(string text, string expected)
    {
        var fields = _sut.Extract(new DocumentContent([new DocumentPage(1, text, "ocr-tesseract")], "ocr-tesseract", []));
        Assert.Equal(expected, fields["InvoiceNo"].NormalizedValue);
    }

    [Fact]
    public void Extract_Does_Not_Mistake_Invoice_Number_For_Date_Or_Subtotal_For_Total()
    {
        var text = "SALES INVOICE\nNo. 0058/10/25/30008\nTotal (Rp. 3,505,562,752\nSub Total 3,158,164,641";
        var fields = _sut.Extract(new DocumentContent([new DocumentPage(1, text, "ocr-tesseract")], "ocr-tesseract", []));
        Assert.Null(fields["InvoiceDate"].NormalizedValue);
        Assert.Equal("3505562752.00", fields["TotalInvoice"].NormalizedValue);
    }

    [Fact]
    public void Extract_Prefers_Primary_Invoice_Total_Over_Appendix_And_Duplicate_Ocr()
    {
        var document = new DocumentContent([
            new DocumentPage(1, "Grand Total: IDR 3,497,945, 749.00\nGrand Total: IDR 3,497,945, 709.00", "ocr-tesseract"),
            new DocumentPage(2, "Grand Total: IDR 1,000.00", "ocr-tesseract")], "ocr-tesseract", []);
        Assert.Equal("3497945749.00", _sut.Extract(document)["TotalInvoice"].NormalizedValue);
    }
}
