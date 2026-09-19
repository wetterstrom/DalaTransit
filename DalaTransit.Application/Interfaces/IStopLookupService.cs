namespace DalaTransit.Application.Interfaces;

public interface IStopLookupService
{
    /// <summary>
    /// Slår upp ett hållplatsnamn baserat på dess ID (t.ex. Rikshållplats-ID).
    /// </summary>
    Task<string> GetStopNameAsync(string stopId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hämtar och synkar stops.txt från Trafiklabs statiska GTFS-arkiv till databasen.
    /// </summary>
    Task SyncStopsFromTrafiklabAsync(CancellationToken cancellationToken = default);
}