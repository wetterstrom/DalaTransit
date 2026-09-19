using DalaTransit.Domain;
using Microsoft.EntityFrameworkCore;

namespace DalaTransit.Infrastructure.Persistence;

public sealed class TransitDbContext : DbContext
{
    public DbSet<ArrivalObservationRecord> Observations => Set<ArrivalObservationRecord>();
    public DbSet<StopRecord> Stops => Set<StopRecord>(); // <-- Denna rad måste finnas

    public TransitDbContext(DbContextOptions<TransitDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ArrivalObservationRecord>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.HasIndex(x => new { x.LineNumber, x.StopId, x.ScheduledArrival });
        });

        modelBuilder.Entity<StopRecord>(builder =>
        {
            builder.HasKey(x => x.StopId);
            builder.HasIndex(x => x.StopName);
        });
    }
}

public class ArrivalObservationRecord
{
    public Guid Id { get; set; }
    public string LineNumber { get; set; } = string.Empty;
    public string StopId { get; set; } = string.Empty;
    public string StopName { get; set; } = string.Empty;
    public DateTime ScheduledArrival { get; set; }
    public DateTime ActualArrival { get; set; }
    public int DelaySeconds { get; set; }
}

public class StopRecord
{
    public string StopId { get; set; } = string.Empty;
    public string StopName { get; set; } = string.Empty;
}