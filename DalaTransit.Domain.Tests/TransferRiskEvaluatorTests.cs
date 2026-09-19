using DalaTransit.Domain;
using Xunit;

namespace DalaTransit.Domain.Tests;

public class TransferRiskEvaluatorTests
{
    [Fact]
    public void Evaluate_ShouldReportHighRisk_WhenMoreThan25PercentMissTransfer()
    {
        // Arrange: 10 observationer. Planerad ankomst 09:32. Bytesmarginal 3 minuter.
        var scheduled = new DateTime(2026, 1, 15, 9, 32, 0);
        var observations = new List<ArrivalObservation>();

        // 6 turer i tid / inom marginalen (0-2 minuter sen)
        for (int i = 0; i < 6; i++)
        {
            observations.Add(new ArrivalObservation(
                Guid.NewGuid(), "151", "stop_knutpunkten", "Falun Knutpunkten",
                scheduled, scheduled.AddMinutes(1)));
        }

        // 4 turer som är 4-6 minuter sena (missar 3-minutersbytet!)
        for (int i = 0; i < 4; i++)
        {
            observations.Add(new ArrivalObservation(
                Guid.NewGuid(), "151", "stop_knutpunkten", "Falun Knutpunkten",
                scheduled, scheduled.AddMinutes(5)));
        }

        // Act
        var report = TransferRiskEvaluator.Evaluate(observations, TimeSpan.FromMinutes(3));

        // Assert
        Assert.Equal(10, report.TotalTripsAnalyzed);
        Assert.Equal(4, report.MissedTransfersCount);
        Assert.Equal(40.0, report.MissRiskPercentage);
        Assert.Equal(RiskCategory.High, report.Category);
    }
}