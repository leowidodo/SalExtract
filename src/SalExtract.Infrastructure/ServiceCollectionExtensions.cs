using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SalExtract.Application;

namespace SalExtract.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSalExtract(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(ExtractionOptions.SectionName);
        services.Configure<ExtractionOptions>(o =>
        {
            if (int.TryParse(section["NativeTextMinimumCharactersPerPage"], out var minimum)) o.NativeTextMinimumCharactersPerPage = minimum;
            if (int.TryParse(section["OcrDpi"], out var dpi)) o.OcrDpi = dpi;
            if (!string.IsNullOrWhiteSpace(section["OcrLanguages"])) o.OcrLanguages = section["OcrLanguages"]!;
            if (!string.IsNullOrWhiteSpace(section["TessDataPath"])) o.TessDataPath = section["TessDataPath"]!;
            if (int.TryParse(section["MaxPages"], out var maxPages)) o.MaxPages = maxPages;
        });
        var connection = configuration.GetConnectionString("SalExtract") ?? "Data Source=data/salextract.db";
        var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connection);
        if (!string.IsNullOrWhiteSpace(builder.DataSource) && builder.DataSource != ":memory:")
        {
            var path = Path.GetFullPath(builder.DataSource, AppContext.BaseDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            builder.DataSource = path;
        }
        services.AddPooledDbContextFactory<ExtractionDbContext>(o => o.UseSqlite(builder.ToString()));
        services.AddScoped<IDocumentTextExtractor, PdfDocumentTextExtractor>();
        services.AddSingleton<IInvoiceFieldExtractor, InvoiceFieldExtractor>();
        services.AddScoped<IExtractionRepository, SqliteExtractionRepository>();
        services.AddScoped<IExtractionService, ExtractionService>();
        return services;
    }
    public static async Task InitializeSalExtractAsync(this IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ExtractionDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
    }
}
