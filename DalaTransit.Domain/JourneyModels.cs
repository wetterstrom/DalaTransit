namespace DalaTransit.Domain;

public sealed record JourneyLeg(
    string LineNumber,
    string OriginStop,
    string OriginPlatform,
    TimeSpan DepartureTime,
    string DestinationStop,
    string DestinationPlatform,
    TimeSpan ArrivalTime,
    string DirectionHeading);

public sealed record JourneyOption(
    string Id,
    TimeSpan StartTime,
    TimeSpan EndTime,
    TimeSpan TotalDuration,
    int TransferCount,
    List<JourneyLeg> Legs,
    TimeSpan? TransferMargin,
    string? TransferStopName,
    TransferRiskReport? RiskAnalysis);