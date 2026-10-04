using Microsoft.EntityFrameworkCore;

namespace SalExtract.Infrastructure;

public sealed class ExtractionRecord
{
    public Guid Id { get; set; }
    public required string FileName { get; set; }
    public required string Sha256 { get; set; }
    public DateTimeOffset ProcessedAtUtc { get; set; }
    public required string Status { get; set; }
    public required string ResultJson { get; set; }
}

public sealed class ExtractionDbContext(DbContextOptions<ExtractionDbContext> options) : DbContext(options)
{
    public DbSet<ExtractionRecord> Extractions => Set<ExtractionRecord>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ExtractionRecord>();
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => x.Sha256);
        entity.Property(x => x.FileName).HasMaxLength(260);
        entity.Property(x => x.Status).HasMaxLength(32);
    }
}
