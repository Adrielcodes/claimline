using System.Text.Json;
using Claimline.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace Claimline.Api.Data;

public sealed class ClaimlineDb(DbContextOptions<ClaimlineDb> options) : DbContext(options)
{
    public const string ClaimNumberSequence = "ClaimNumbers";

    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<ClaimEvent> ClaimEvents => Set<ClaimEvent>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    public async Task<string> NextClaimNumberAsync(DateTimeOffset now, CancellationToken ct)
    {
        // Raw ADO.NET: SQL Server rejects NEXT VALUE FOR inside the subquery EF would wrap around SqlQuery.
        await Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT NEXT VALUE FOR dbo.{ClaimNumberSequence}";
            command.Transaction = Database.CurrentTransaction?.GetDbTransaction();
            var next = Convert.ToInt64(await command.ExecuteScalarAsync(ct));
            return $"CLM-{now:yyyy}-{next:D6}";
        }
        finally
        {
            await Database.CloseConnectionAsync();
        }
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasSequence<long>(ClaimNumberSequence).StartsAt(1001);

        b.Entity<Policy>(e =>
        {
            e.HasIndex(p => p.PolicyNumber).IsUnique();
            e.Property(p => p.PolicyNumber).HasMaxLength(20);
            e.Property(p => p.InsuredFirstName).HasMaxLength(60);
            e.Property(p => p.InsuredLastName).HasMaxLength(60);
            e.Property(p => p.InsuredEmail).HasMaxLength(120);
            e.Property(p => p.State).HasMaxLength(2);
            e.Property(p => p.Vehicle).HasMaxLength(80);
            e.Property(p => p.DemoScenario).HasMaxLength(200);
            e.Property(p => p.CollisionDeductible).HasPrecision(12, 2);
            e.Property(p => p.VehicleValue).HasPrecision(12, 2);
        });

        b.Entity<Claim>(e =>
        {
            e.HasIndex(c => c.ClaimNumber).IsUnique();
            e.HasIndex(c => c.IdempotencyKey).IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL");
            e.HasIndex(c => new { c.Status, c.ReportedAt });
            e.Property(c => c.ClaimNumber).HasMaxLength(20);
            e.Property(c => c.LossLocation).HasMaxLength(200);
            e.Property(c => c.Description).HasMaxLength(4000);
            e.Property(c => c.ContactPhone).HasMaxLength(30);
            e.Property(c => c.IdempotencyKey).HasMaxLength(64);
            e.Property(c => c.Analyzer).HasMaxLength(40);
            e.Property(c => c.LossType).HasConversion<string>().HasMaxLength(20);
            e.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(c => c.Route).HasConversion<string>().HasMaxLength(30);
            e.Property(c => c.EstimatedAmount).HasPrecision(12, 2);
            e.Property(c => c.ReserveAmount).HasPrecision(12, 2);
            e.Property(c => c.RowVersion).IsRowVersion();
            e.Property(c => c.Analysis).HasConversion(JsonConverter<ClaimAnalysis?>());
            e.Property(c => c.Signals).HasConversion(JsonConverter<List<TriageSignal>>(), JsonComparer<List<TriageSignal>>());
            e.HasOne(c => c.Policy).WithMany().HasForeignKey(c => c.PolicyId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(c => c.Timeline).WithOne().HasForeignKey(t => t.ClaimId);
        });

        b.Entity<ClaimEvent>(e =>
        {
            e.HasIndex(t => new { t.ClaimId, t.At });
            e.Property(t => t.Type).HasMaxLength(40);
            e.Property(t => t.Title).HasMaxLength(200);
            e.Property(t => t.Detail).HasMaxLength(1000);
        });

        b.Entity<OutboxMessage>(e =>
        {
            e.ToTable("Outbox");
            e.HasIndex(o => o.SentAt).HasFilter("[SentAt] IS NULL");
            e.Property(o => o.EventType).HasMaxLength(60);
            e.Property(o => o.CorrelationId).HasMaxLength(60);
            e.Property(o => o.LastError).HasMaxLength(1000);
        });

        b.Entity<ProcessedMessage>(e =>
        {
            e.HasKey(p => new { p.Consumer, p.MessageId });
            e.Property(p => p.Consumer).HasMaxLength(60);
            e.Property(p => p.MessageId).HasMaxLength(100);
        });
    }

    // JSON columns via value converters keep the model portable and the enum-as-string format identical to the wire format.
    private static Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<T, string> JsonConverter<T>() => new(
        v => JsonSerializer.Serialize(v, ClaimlineJson.Options),
        v => JsonSerializer.Deserialize<T>(v, ClaimlineJson.Options)!);

    private static ValueComparer<T> JsonComparer<T>() => new(
        (a, b) => JsonSerializer.Serialize(a, ClaimlineJson.Options) == JsonSerializer.Serialize(b, ClaimlineJson.Options),
        v => JsonSerializer.Serialize(v, ClaimlineJson.Options).GetHashCode(),
        v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, ClaimlineJson.Options), ClaimlineJson.Options)!);
}
