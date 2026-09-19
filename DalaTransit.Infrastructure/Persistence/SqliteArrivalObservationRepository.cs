using DalaTransit.Application.Interfaces;
using DalaTransit.Domain;
using Microsoft.EntityFrameworkCore;

namespace DalaTransit.Infrastructure.Persistence;

public sealed class SqliteArrivalObservationRepository : IArrivalObservationRepository
{
    private readonly TransitDbContext _context;

    public SqliteArrivalObservationRepository(TransitDbContext context)
    {
        _context = context;
    }

    public async Task<IngestionResult> SaveBatchAsync(IEnumerable<ArrivalObservation> observations, CancellationToken cancellationToken = default)
    {
        var obsList = observations.ToList();
        if (obsList.Count == 0) return new IngestionResult(0, 0);

        // Hämta tidsfönstret för batchen för snabb indexsökning i SQLite
        var minTime = obsList.Min(o => o.ScheduledArrival).AddMinutes(-30);
        var maxTime = obsList.Max(o => o.ScheduledArrival).AddMinutes(30);

        var existingRecords = await _context.Observations
            .Where(x => x.ScheduledArrival >= minTime && x.ScheduledArrival <= maxTime)
            .ToListAsync(cancellationToken);

        // Skapa en snabbuppslagsnyckel: Linje + Hållplats + Tidtabellstid
        var existingLookup = existingRecords.ToDictionary(
            x => $"{x.LineNumber}_{x.StopId}_{x.ScheduledArrival:yyyyMMddHHmmss}");

        int inserted = 0;
        int updated = 0;

        foreach (var obs in obsList)
        {
            var key = $"{obs.LineNumber}_{obs.StopId}_{obs.ScheduledArrival:yyyyMMddHHmmss}";

            if (existingLookup.TryGetValue(key, out var existing))
            {
                // Uppdatera endast om förseningen/prognosen har ändrats
                if (existing.ActualArrival != obs.ActualArrival || existing.DelaySeconds != (int)obs.Delay.TotalSeconds)
                {
                    existing.ActualArrival = obs.ActualArrival;
                    existing.DelaySeconds = (int)obs.Delay.TotalSeconds;
                    updated++;
                }
            }
            else
            {
                var newRecord = new ArrivalObservationRecord
                {
                    Id = obs.Id,
                    LineNumber = obs.LineNumber,
                    StopId = obs.StopId,
                    StopName = obs.StopName,
                    ScheduledArrival = obs.ScheduledArrival,
                    ActualArrival = obs.ActualArrival,
                    DelaySeconds = (int)obs.Delay.TotalSeconds
                };

                await _context.Observations.AddAsync(newRecord, cancellationToken);
                existingLookup[key] = newRecord;
                inserted++;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        return new IngestionResult(inserted, updated);
    }

    public async Task<IReadOnlyList<ArrivalObservation>> GetByStopIdAsync(string stopId, CancellationToken cancellationToken = default)
    {
        var records = await _context.Observations
            .AsNoTracking()
            .Where(x => x.StopId == stopId)
            .ToListAsync(cancellationToken);

        return MapToDomain(records);
    }

    public async Task<IReadOnlyList<ArrivalObservation>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var records = await _context.Observations
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return MapToDomain(records);
    }

    private static IReadOnlyList<ArrivalObservation> MapToDomain(List<ArrivalObservationRecord> records)
    {
        return records.Select(r => new ArrivalObservation(
            id: r.Id,
            lineNumber: r.LineNumber,
            stopId: r.StopId,
            stopName: r.StopName,
            scheduledArrival: r.ScheduledArrival,
            actualArrival: r.ActualArrival
        )).ToList();
    }
}