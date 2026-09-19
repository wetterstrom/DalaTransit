using DalaTransit.Application.Interfaces;
using DalaTransit.Domain;

namespace DalaTransit.Application.UseCases;

public sealed class AnalyzeTransferRiskUseCase
{
    private readonly IArrivalObservationRepository _repository;

    public AnalyzeTransferRiskUseCase(IArrivalObservationRepository repository)
    {
        _repository = repository;
    }

    public async Task<TransferRiskReport> ExecuteAsync(
        string? stopId,
        TimeSpan transferMargin,
        CancellationToken cancellationToken = default)
    {
        var observations = string.IsNullOrWhiteSpace(stopId)
            ? await _repository.GetAllAsync(cancellationToken)
            : await _repository.GetByStopIdAsync(stopId, cancellationToken);

        // Anropar den statiska domänmotorn direkt med observationerna och bytesmarginalen
        return TransferRiskEvaluator.Evaluate(observations, transferMargin);
    }
}