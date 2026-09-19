using System.Text.Json.Serialization;

namespace DalaTransit.Domain;

public sealed class RiskExportData
{
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public int TotalDatabaseRecords { get; set; }
    public List<StopRiskSummary> Summaries { get; set; } = new();
}

public sealed class StopRiskSummary
{
    public string StopId { get; set; } = string.Empty;
    public string StopName { get; set; } = string.Empty;
    public int MarginMinutes { get; set; }
    public TransferRiskReport Report { get; set; } = null!;
}