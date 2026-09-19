namespace DalaTransit.Domain;

/// <summary>
/// Representerar ett enskilt historiskt utfall vid en hållplats.
/// </summary>
public sealed class ArrivalObservation
{
    public Guid Id { get; }
    public string LineNumber { get; }
    public string StopId { get; }
    public string StopName { get; }
    public DateTime ScheduledArrival { get; }
    public DateTime ActualArrival { get; }
    public Delay Delay { get; }
    public DayOfWeek DayOfWeek => ScheduledArrival.DayOfWeek;
    public TimeOnly ScheduledTime => TimeOnly.FromDateTime(ScheduledArrival);

    public ArrivalObservation(
        Guid id,
        string lineNumber,
        string stopId,
        string stopName,
        DateTime scheduledArrival,
        DateTime actualArrival)
    {
        if (string.IsNullOrWhiteSpace(lineNumber))
            throw new ArgumentException("Linjenummer måste anges.", nameof(lineNumber));
        if (string.IsNullOrWhiteSpace(stopId))
            throw new ArgumentException("Hållplats-ID måste anges.", nameof(stopId));

        Id = id;
        LineNumber = lineNumber;
        StopId = stopId;
        StopName = stopName;
        ScheduledArrival = scheduledArrival;
        ActualArrival = actualArrival;
        Delay = Delay.FromDifference(scheduledArrival, actualArrival);
    }
}
