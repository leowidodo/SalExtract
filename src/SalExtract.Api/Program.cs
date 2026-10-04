using System.Threading.RateLimiting;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using SalExtract.Application;
using SalExtract.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
const long maxUploadBytes = 25 * 1024 * 1024;
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = maxUploadBytes);
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHealthChecks();
builder.Services.AddSalExtract(builder.Configuration);
builder.Services.AddRateLimiter(o => o.AddPolicy("extraction", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 2 })));

var app = builder.Build();
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    context.Response.StatusCode = error is InvalidDataException ? StatusCodes.Status400BadRequest : StatusCodes.Status500InternalServerError;
    await Results.Problem(statusCode: context.Response.StatusCode, title: error is InvalidDataException ? "Invalid document" : "Document processing failed", detail: app.Environment.IsDevelopment() ? error?.Message : null).ExecuteAsync(context);
}));
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    var expected = context.RequestServices.GetRequiredService<IConfiguration>()["Security:ApiKey"];
    if (context.Request.Path.StartsWithSegments("/api/v1") && !string.IsNullOrWhiteSpace(expected) && !SecretEquals(context.Request.Headers["X-Api-Key"].ToString(), expected))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    await next(context);
});
app.MapGet("/", () => Results.Ok(new { service = "SalExtract.Api", version = "v1", offline = true }));
app.MapHealthChecks("/health");
app.MapPost("/api/v1/extractions", async (IFormFile file, IExtractionService service, CancellationToken ct) =>
{
    if (file.Length == 0) return Results.BadRequest(new { error = "PDF file is empty." });
    if (file.Length > maxUploadBytes) return Results.BadRequest(new { error = "Maximum PDF size is 25 MB." });
    if (!string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest(new { error = "Only .pdf files are accepted." });
    await using var stream = file.OpenReadStream();
    var result = await service.ExtractAsync(stream, Path.GetFileName(file.FileName), ct);
    return Results.Created($"/api/v1/extractions/{result.Id}", result);
}).DisableAntiforgery().RequireRateLimiting("extraction");
app.MapGet("/api/v1/extractions/{id:guid}", async (Guid id, IExtractionRepository repository, CancellationToken ct) => await repository.FindAsync(id, ct) is { } result ? Results.Ok(result) : Results.NotFound());
app.MapGet("/api/v1/extractions", async (int? limit, IExtractionRepository repository, CancellationToken ct) => Results.Ok(await repository.ListAsync(limit ?? 20, ct)));
await app.Services.InitializeSalExtractAsync();
app.Run();

static bool SecretEquals(string provided, string expected)
{
    var left = Encoding.UTF8.GetBytes(provided);
    var right = Encoding.UTF8.GetBytes(expected);
    return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
}

public partial class Program;
