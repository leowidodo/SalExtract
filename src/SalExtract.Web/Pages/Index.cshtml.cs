using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SalExtract.Web.Pages;

public sealed class IndexModel(IHttpClientFactory clients, ILogger<IndexModel> logger) : PageModel
{
    [BindProperty] public IFormFile? Pdf { get; set; }
    public JsonElement? Result { get; private set; }
    public string? Error { get; private set; }
    public void OnGet() { }
    public async Task OnPostAsync(CancellationToken cancellationToken)
    {
        if (Pdf is null || Pdf.Length == 0) { Error = "Pilih file PDF terlebih dahulu."; return; }
        if (Pdf.Length > 25 * 1024 * 1024) { Error = "Ukuran maksimum file adalah 25 MB."; return; }
        try
        {
            using var form = new MultipartFormDataContent();
            await using var input = Pdf.OpenReadStream();
            using var content = new StreamContent(input);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(content, "file", Path.GetFileName(Pdf.FileName));
            using var response = await clients.CreateClient("SalExtractApi").PostAsync("/api/v1/extractions", form, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) { Error = $"API mengembalikan {(int)response.StatusCode}: {json}"; return; }
            Result = JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (HttpRequestException ex) { logger.LogWarning(ex, "SalExtract API unavailable"); Error = "SalExtract.Api tidak dapat dihubungi. Jalankan profile multiple startup projects di Visual Studio."; }
    }
}
