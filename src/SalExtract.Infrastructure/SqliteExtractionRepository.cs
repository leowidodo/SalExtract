using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SalExtract.Application;
using SalExtract.Domain;

namespace SalExtract.Infrastructure;

public sealed class SqliteExtractionRepository(IDbContextFactory<ExtractionDbContext> contextFactory) : IExtractionRepository
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
    public async Task SaveAsync(ExtractionResult result, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        db.Extractions.Add(new ExtractionRecord { Id = result.Id, FileName = result.FileName, Sha256 = result.Sha256, ProcessedAtUtc = result.ProcessedAtUtc, Status = result.Status.ToString(), ResultJson = JsonSerializer.Serialize(result, JsonOptions) });
        await db.SaveChangesAsync(cancellationToken);
    }
    public async Task<ExtractionResult?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var json = await db.Extractions.Where(x => x.Id == id).Select(x => x.ResultJson).SingleOrDefaultAsync(cancellationToken);
        return json is null ? null : JsonSerializer.Deserialize<ExtractionResult>(json, JsonOptions);
    }
    public async Task<IReadOnlyList<ExtractionResult>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var take = Math.Clamp(limit, 1, 100);

        // SQLite stores DateTimeOffset as TEXT and cannot translate ORDER BY for that CLR type.
        // Read only lightweight metadata first, order it in memory, then fetch JSON for the
        // selected records. This keeps result payload loading bounded to the requested page.
        var metadata = await db.Extractions.AsNoTracking()
            .Select(x => new { x.Id, x.ProcessedAtUtc })
            .ToListAsync(cancellationToken);
        var orderedIds = metadata.OrderByDescending(x => x.ProcessedAtUtc)
            .Take(take)
            .Select(x => x.Id)
            .ToArray();
        if (orderedIds.Length == 0) return [];

        var jsonById = await db.Extractions.AsNoTracking()
            .Where(x => orderedIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.ResultJson, cancellationToken);
        return orderedIds
            .Select(id => JsonSerializer.Deserialize<ExtractionResult>(jsonById[id], JsonOptions)!)
            .ToList();
    }
}
