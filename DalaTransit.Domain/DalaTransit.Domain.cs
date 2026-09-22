using System.Text.Json.Serialization;

namespace DalaTransit.Domain;

public sealed class RiskExportData
{
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public int TotalDatabaseRecords { get; set; }
    public List<StopRiskSummary> Summaries { get; set; } = new();
}

public record StopRiskSummary
{
    public string StopId { get; init; } = "";
    public string StopName { get; init; } = "";
    public int MarginMinutes { get; init; }
    public TransferRiskReport Report { get; init; } = null!;
    public int? HourOfDay { get; init; }

    public StopRiskSummary() { }

    public StopRiskSummary(string stopId, string stopName, int marginMinutes, TransferRiskReport report, int? hourOfDay = null)
    {
        StopId = stopId;
        StopName = stopName;
        MarginMinutes = marginMinutes;
        Report = report;
        HourOfDay = hourOfDay;
    }
}