using DalaTransit.Domain;

namespace DalaTransit.Application.Interfaces;

public sealed record IngestionResult(int Inserted, int Updated);

public interface IArrivalObservationRepository
{
    Task<IngestionResult> SaveBatchAsync(IEnumerable<ArrivalObservation> observations, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ArrivalObservation>> GetByStopIdAsync(string stopId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ArrivalObservation>> GetAllAsync(CancellationToken cancellationToken = default);
}