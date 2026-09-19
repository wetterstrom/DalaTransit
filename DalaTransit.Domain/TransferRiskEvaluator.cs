using System;
using System.Collections.Generic;
using System.Text;

namespace DalaTransit.Domain
{
    public sealed record TransferRiskReport(
    int TotalTripsAnalyzed,
    int MissedTransfersCount,
    double MissRiskPercentage,
    double MedianDelayMinutes,
    double P90DelayMinutes,
    RiskCategory Category);

    public enum RiskCategory
    {
        Low,       // < 10% risk
        Moderate,  // 10% - 25% risk
        High,      // 25% - 50% risk
        Critical   // > 50% risk
    }

    /// <summary>
    /// Domäntjänst som beräknar sannolikheten för brutet byte (broken connection)
    /// baserat på historisk ankomstvarians och tillgänglig bytesmarginal.
    /// </summary>
    public static class TransferRiskEvaluator
    {
        /// <param name="inboundArrivals">Historiska ankomster för Buss 1 (t.ex. Hellbergsväg -> Knutpunkten)</param>
        /// <param name="transferWindow">Planerad marginal mellan ankomst och nästa avgång (t.ex. 3 minuter = 09:32 till 09:35)</param>
        public static TransferRiskReport Evaluate(
            IReadOnlyCollection<ArrivalObservation> inboundArrivals,
            TimeSpan transferWindow)
        {
            if (inboundArrivals.Count == 0)
            {
                return new TransferRiskReport(0, 0, 0, 0, 0, RiskCategory.Low);
            }

            var delaysInMinutes = inboundArrivals
                .Select(o => o.Delay.TotalMinutes)
                .OrderBy(m => m)
                .ToList();

            // Ett byte missas om förseningen överstiger bytesmarginalen
            var transferWindowMinutes = transferWindow.TotalMinutes;
            var missedCount = delaysInMinutes.Count(delay => delay > transferWindowMinutes);
            var riskPercentage = Math.Round((double)missedCount / inboundArrivals.Count * 100, 1);

            // Beräkna median (P50) och 90:e percentilen (P90)
            var median = CalculatePercentile(delaysInMinutes, 50);
            var p90 = CalculatePercentile(delaysInMinutes, 90);

            var category = riskPercentage switch
            {
                < 10.0 => RiskCategory.Low,
                <= 25.0 => RiskCategory.Moderate,
                <= 50.0 => RiskCategory.High,
                _ => RiskCategory.Critical
            };

            return new TransferRiskReport(
                TotalTripsAnalyzed: inboundArrivals.Count,
                MissedTransfersCount: missedCount,
                MissRiskPercentage: riskPercentage,
                MedianDelayMinutes: median,
                P90DelayMinutes: p90,
                Category: category);
        }

        private static double CalculatePercentile(List<double> sortedValues, int percentile)
        {
            if (sortedValues.Count == 0) return 0;
            int index = (int)Math.Ceiling(percentile / 100.0 * sortedValues.Count) - 1;
            index = Math.Clamp(index, 0, sortedValues.Count - 1);
            return Math.Round(sortedValues[index], 1);
        }
    }
}
