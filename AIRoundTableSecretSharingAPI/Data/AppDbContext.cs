using System.Text.Json;
using AIRoundTableSecretSharingCommon.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AIRoundTableSecretSharingAPI.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<ProducerInfo> Producers => Set<ProducerInfo>();
    public DbSet<ProducerEpoch> Epochs => Set<ProducerEpoch>();
    public DbSet<MetricSubmission> Submissions => Set<MetricSubmission>();
    public DbSet<PartnerPublicKey> PublicKeys => Set<PartnerPublicKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ProducerInfo — ProducerId is the natural key
        modelBuilder.Entity<ProducerInfo>(e =>
        {
            e.HasKey(p => p.ProducerId);
            e.Property(p => p.ProducerId).HasMaxLength(100);
            e.Property(p => p.DisplayName).HasMaxLength(200);
        });

        // ProducerEpoch — ProducerIds stored as JSON, EpochId is the PK
        var listConverter = new ValueConverter<List<string>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()
        );
        var listComparer = new ValueComparer<List<string>>(
            (a, b) => a != null && b != null && a.SequenceEqual(b),
            v => v.Aggregate(0, (acc, s) => HashCode.Combine(acc, s.GetHashCode())),
            v => v.ToList()
        );

        modelBuilder.Entity<ProducerEpoch>(e =>
        {
            e.HasKey(ep => ep.EpochId);
            // EpochId is an application-assigned value, not a DB-generated identity
            e.Property(ep => ep.EpochId).ValueGeneratedNever();
            e.Property(ep => ep.ProducerIds)
                .HasConversion(listConverter, listComparer)
                .HasColumnType("nvarchar(max)");
        });

        // MetricSubmission — no PK in the model, use a shadow int identity column
        modelBuilder.Entity<MetricSubmission>(e =>
        {
            e.Property<int>("Id").UseIdentityColumn();
            e.HasKey("Id");
            e.Property(s => s.ProducerId).HasMaxLength(100);
            e.Property(s => s.Country).HasMaxLength(100);
            e.Property(s => s.Signature).HasMaxLength(500);
            // Unique constraint mirrors duplicate-detection logic in the original store
            e.HasIndex(s => new { s.ProducerId, s.Country, s.Month, s.EpochId }).IsUnique();
        });

        // PartnerPublicKey — ProducerId is the natural key; upsert replaces on re-register
        modelBuilder.Entity<PartnerPublicKey>(e =>
        {
            e.HasKey(k => k.ProducerId);
            e.Property(k => k.ProducerId).HasMaxLength(100);
            e.Property(k => k.PublicKeyBase64).HasColumnType("nvarchar(max)");
        });
    }
}
