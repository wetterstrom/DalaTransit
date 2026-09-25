using System;
using System.Collections.Generic;
using System.Text;

namespace DalaTransit.Domain
{
    public class PunctualityReport
    {
        public DateTime LastUpdated { get; set; }
        public double OverallOnTimePercentage { get; set; }
        public double TransferSuccessRate { get; set; }
        public int TotalMonitoredTrips { get; set; }
        public List<LinePerformance> Lines { get; set; } = new();
        public List<HighRiskTransfer> RiskTransfers { get; set; } = new();
    }

    public class LinePerformance
    {
        public string LineNumber { get; set; } = string.Empty;
        public string RouteName { get; set; } = string.Empty;
        public double OnTimePercentage { get; set; }
        public double AverageDelayMinutes { get; set; }
    }

    public class HighRiskTransfer
    {
        public string StationName { get; set; } = string.Empty;
        public string IncomingLine { get; set; } = string.Empty;
        public string ConnectingLine { get; set; } = string.Empty;
        public int ScheduledMarginMinutes { get; set; }
        public int MissRiskPercentage { get; set; }
    }
}
