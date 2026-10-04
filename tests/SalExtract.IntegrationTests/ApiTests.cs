using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SalExtract.Application;
using SalExtract.Infrastructure;

namespace SalExtract.IntegrationTests;

public sealed class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    public ApiTests(WebApplicationFactory<Program> factory) => _client = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:SalExtract", "Data Source=integration-tests.db")).CreateClient();

    [Fact]
    public async Task Health_Returns_Success() => Assert.True((await _client.GetAsync("/health")).IsSuccessStatusCode);

    [Fact]
    public async Task Extraction_List_Returns_Success()
    {
        var response = await _client.GetAsync("/api/v1/extractions?limit=20");
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Extraction_Rejects_NonPdf()
    {
        using var form = new MultipartFormDataContent { { new ByteArrayContent("hello"u8.ToArray()), "file", "unsafe.txt" } };
        var response = await _client.PostAsync("/api/v1/extractions", form);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Extraction_Recognizes_Bengkel_Scanned_Invoice()
    {
        if (!OperatingSystem.IsWindows()) return;
        var repositoryRoot = Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
        var options = Options.Create(new ExtractionOptions { TessDataPath = Path.Combine(repositoryRoot, "tools/tessdata") });
        var extractor = new PdfDocumentTextExtractor(options, NullLogger<PdfDocumentTextExtractor>.Instance);
        var pdfPath = Path.Combine(repositoryRoot, "Samples/Invoice - Bengkel Motor Abadi (Scanned).pdf");
        await using var pdf = File.OpenRead(pdfPath);
        var document = await extractor.ExtractAsync(pdf, CancellationToken.None);
        var fields = new InvoiceFieldExtractor().Extract(document);

        Assert.Equal("102/BMA/26", fields["InvoiceNo"].NormalizedValue);
        Assert.Equal("2026-10-01", fields["InvoiceDate"].NormalizedValue);
        Assert.Equal("160000.00", fields["TotalInvoice"].NormalizedValue);
        Assert.Equal("IDR", fields["Currency"].NormalizedValue);
    }

    [Fact]
    public async Task Extraction_Recognizes_Freelancer_Stacked_Total_Layout()
    {
        var repositoryRoot = Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
        var pdfPath = Path.Combine(repositoryRoot, "Samples/Invoice - Freelancer Budi.pdf");
        var options = Options.Create(new ExtractionOptions { TessDataPath = Path.Combine(repositoryRoot, "tools/tessdata") });
        var extractor = new PdfDocumentTextExtractor(options, NullLogger<PdfDocumentTextExtractor>.Instance);
        await using var pdf = File.OpenRead(pdfPath);
        var document = await extractor.ExtractAsync(pdf, CancellationToken.None);
        var fields = new InvoiceFieldExtractor().Extract(document);

        Assert.Equal("native-pdf", document.Method);
        Assert.Equal("1025.00", fields["TotalInvoice"].NormalizedValue);
        Assert.Equal("Total Due -> $1,025.00", fields["TotalInvoice"].Evidence);
        Assert.Equal("USD", fields["Currency"].NormalizedValue);
    }

    [Fact]
    public async Task Extraction_Persists_And_Returns_Currency()
    {
        var repositoryRoot = Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
        var pdfPath = Path.Combine(repositoryRoot, "Samples/Invoice - Freelancer Budi.pdf");
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(await File.ReadAllBytesAsync(pdfPath)), "file", "Invoice - Freelancer Budi.pdf" }
        };

        using var created = await _client.PostAsync("/api/v1/extractions", form);
        Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
        var location = Assert.IsType<Uri>(created.Headers.Location);

        using var stored = await _client.GetAsync(location);
        stored.EnsureSuccessStatusCode();
        using var payload = System.Text.Json.JsonDocument.Parse(await stored.Content.ReadAsStringAsync());
        var currency = payload.RootElement.GetProperty("fields").GetProperty("Currency");
        Assert.Equal("USD", currency.GetProperty("normalizedValue").GetString());
        Assert.Equal("$", currency.GetProperty("rawValue").GetString());
    }
}
