using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DalaTransit.Domain;
using DalaTransit.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using ProtoBuf;
using TransitRealtime;

namespace DalaTransit.Infrastructure.External;

public sealed record GtfsFeedResult(
    int RawEntityCount,
    IReadOnlyList<ArrivalObservation> Observations);

public sealed class TrafiklabGtfsRtClient
{
    private readonly HttpClient _httpClient;
    private readonly TrafiklabOptions _options;

    public TrafiklabGtfsRtClient(HttpClient httpClient, IOptions<TrafiklabOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<GtfsFeedResult> FetchCurrentArrivalsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(_options.FeedUrlTemplate, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        var feed = Serializer.Deserialize<FeedMessage>(stream);
        var observations = new List<ArrivalObservation>();

        if (feed?.Entities == null)
            return new GtfsFeedResult(0, observations);

        var timestampSeconds = feed.Header != null ? (long)feed.Header.Timestamp : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var feedTimestamp = DateTimeOffset.FromUnixTimeSeconds(timestampSeconds).LocalDateTime;

        foreach (var entity in feed.Entities)
        {
            var tripUpdate = entity.TripUpdate;
            if (tripUpdate?.StopTimeUpdates == null) continue;

            // Fallback: om RouteId saknas, använd TripId eller entitetens ID
            var tripDescriptor = tripUpdate.Trip;
            var routeId = !string.IsNullOrWhiteSpace(tripDescriptor?.RouteId)
                ? tripDescriptor.RouteId
                : (!string.IsNullOrWhiteSpace(tripDescriptor?.TripId) ? $"Tur {tripDescriptor.TripId}" : $"Entitet {entity.Id}");

            foreach (var stopUpdate in tripUpdate.StopTimeUpdates)
            {
                var timeEvent = stopUpdate.Arrival ?? stopUpdate.Departure;
                if (timeEvent == null) continue;

                var delaySeconds = timeEvent.Delay;
                var eventTime = timeEvent.Time > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(timeEvent.Time).LocalDateTime
                    : feedTimestamp;

                var scheduledTime = eventTime.AddSeconds(-delaySeconds);

                // Fallback: om StopId saknas, använd StopSequence
                var stopId = !string.IsNullOrWhiteSpace(stopUpdate.StopId)
                    ? stopUpdate.StopId
                    : $"Seq-{stopUpdate.StopSequence}";

                try
                {
                    var observation = new ArrivalObservation(
                        id: Guid.NewGuid(),
                        lineNumber: routeId,
                        stopId: stopId,
                        stopName: $"Hållplats {stopId}",
                        scheduledArrival: scheduledTime,
                        actualArrival: eventTime);

                    observations.Add(observation);
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine($"[Ignorerad rad] Valideringsfel: {ex.Message}");
                    Console.ResetColor();
                }
            }
        }

        return new GtfsFeedResult(feed.Entities.Count, observations);
    }
}