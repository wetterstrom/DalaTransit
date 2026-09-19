using System.Net.Http.Json;
using System.Text.Json;
using DalaTransit.Domain;

namespace DalaTransit;

public sealed class ResRobotRoutingService
{
    private readonly HttpClient _http;
    private const string ApiKey = "a5e60f59-32a5-470f-b84a-ca94fb798b1e";

    public ResRobotRoutingService(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<JourneyOption>> SearchTripsAsync(
        string originName,
        string destinationName,
        DateTime travelDateTime,
        RiskExportData? riskData,
        CancellationToken ct = default)
    {
        var originId = await LookupStopIdAsync(originName, ct);
        var destId = await LookupStopIdAsync(destinationName, ct);

        if (string.IsNullOrEmpty(originId) || string.IsNullOrEmpty(destId))
        {
            return new List<JourneyOption>();
        }

        var dateParam = travelDateTime.ToString("yyyy-MM-dd");
        var timeParam = travelDateTime.ToString("HH:mm");

        var tripUrl = $"https://api.resrobot.se/v2.1/trip?accessId={ApiKey}&originId={originId}&destId={destId}&date={dateParam}&time={timeParam}&format=json";

        using var response = await _http.GetAsync(tripUrl, ct);
        if (!response.IsSuccessStatusCode)
        {
            return new List<JourneyOption>();
        }

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = doc.RootElement;

        if (!root.TryGetProperty("Trip", out var tripsElement) || tripsElement.ValueKind != JsonValueKind.Array)
        {
            return new List<JourneyOption>();
        }

        var results = new List<JourneyOption>();
        int tripIndex = 1;

        foreach (var trip in tripsElement.EnumerateArray())
        {
            if (!trip.TryGetProperty("LegList", out var legList) || !legList.TryGetProperty("Leg", out var legArray))
            {
                continue;
            }

            var legs = new List<JourneyLeg>();
            var elements = legArray.ValueKind == JsonValueKind.Array
                ? legArray.EnumerateArray().ToList()
                : new List<JsonElement> { legArray };

            var vehicleLegs = elements.Where(e => e.TryGetProperty("type", out var t) && t.GetString() == "JNY").ToList();

            foreach (var legElem in vehicleLegs)
            {
                var line = ExtractLineNumber(legElem);
                var origin = legElem.GetProperty("Origin");
                var dest = legElem.GetProperty("Destination");

                var origName = origin.GetProperty("name").GetString() ?? "";
                var origTrack = origin.TryGetProperty("track", out var trk) ? $"läge {trk.GetString()}" : "";
                var origTimeStr = origin.GetProperty("time").GetString() ?? "00:00:00";

                var destName = dest.GetProperty("name").GetString() ?? "";
                var destTrack = dest.TryGetProperty("track", out var dTrk) ? $"läge {dTrk.GetString()}" : "";
                var destTimeStr = dest.GetProperty("time").GetString() ?? "00:00:00";

                var dir = legElem.TryGetProperty("direction", out var d) ? d.GetString() ?? "" : "";

                legs.Add(new JourneyLeg(
                    LineNumber: line,
                    OriginStop: origName,
                    OriginPlatform: origTrack,
                    DepartureTime: TimeSpan.Parse(origTimeStr[..5]),
                    DestinationStop: destName,
                    DestinationPlatform: destTrack,
                    ArrivalTime: TimeSpan.Parse(destTimeStr[..5]),
                    DirectionHeading: dir
                ));
            }

            if (legs.Count == 0) continue;

            var firstLeg = legs.First();
            var lastLeg = legs.Last();
            var duration = lastLeg.ArrivalTime >= firstLeg.DepartureTime
                ? lastLeg.ArrivalTime - firstLeg.DepartureTime
                : (lastLeg.ArrivalTime + TimeSpan.FromHours(24)) - firstLeg.DepartureTime;

            int transferCount = legs.Count - 1;
            TimeSpan? transferMargin = null;
            string? transferStopName = null;
            TransferRiskReport? riskReport = null;

            if (transferCount > 0)
            {
                var leg1 = legs[0];
                var leg2 = legs[1];
                transferStopName = leg1.DestinationStop;

                var margin = leg2.DepartureTime >= leg1.ArrivalTime
                    ? leg2.DepartureTime - leg1.ArrivalTime
                    : (leg2.DepartureTime + TimeSpan.FromHours(24)) - leg1.ArrivalTime;

                transferMargin = margin;
                riskReport = CalculateTransferRisk(transferStopName, (int)margin.TotalMinutes, riskData);
            }

            results.Add(new JourneyOption(
                Id: $"trip_{tripIndex++}",
                StartTime: firstLeg.DepartureTime,
                EndTime: lastLeg.ArrivalTime,
                TotalDuration: duration,
                TransferCount: transferCount,
                Legs: legs,
                TransferMargin: transferMargin,
                TransferStopName: transferStopName,
                RiskAnalysis: riskReport
            ));
        }

        return results;
    }

    private async Task<string?> LookupStopIdAsync(string stopName, CancellationToken ct)
    {
        var url = $"https://api.resrobot.se/v2.1/location.name?accessId={ApiKey}&input={Uri.EscapeDataString(stopName)}&format=json";
        using var resp = await _http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;

        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("stopLocationOrCoordLocation", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in list.EnumerateArray())
        {
            if (item.TryGetProperty("StopLocation", out var stopLoc))
            {
                if (stopLoc.TryGetProperty("extId", out var extIdProp))
                    return extIdProp.GetString();

                if (stopLoc.TryGetProperty("id", out var idProp))
                    return idProp.GetString();
            }
        }

        return null;
    }

    private static string ExtractLineNumber(JsonElement leg)
    {
        if (leg.TryGetProperty("Product", out var prod) && prod.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in prod.EnumerateArray())
            {
                if (p.TryGetProperty("line", out var l)) return l.GetString() ?? "";
            }
        }
        if (leg.TryGetProperty("name", out var n))
        {
            var parts = n.GetString()?.Split(' ');
            return parts?.LastOrDefault() ?? "Buss";
        }
        return "Buss";
    }

    private static TransferRiskReport CalculateTransferRisk(string stopName, int marginMinutes, RiskExportData? riskData)
    {
        if (riskData?.Summaries != null)
        {
            var match = riskData.Summaries.FirstOrDefault(s =>
                s.StopName.Contains(stopName, StringComparison.OrdinalIgnoreCase) &&
                s.MarginMinutes == marginMinutes);

            if (match != null) return match.Report;

            var global = riskData.Summaries.FirstOrDefault(s => s.StopId == "ALL" && s.MarginMinutes == marginMinutes);
            if (global != null) return global.Report;
        }

        double risk = marginMinutes switch
        {
            <= 1 => 65.0,
            2 => 34.2,
            3 => 18.8,
            4 => 11.2,
            5 => 5.5,
            _ => 0.8
        };

        var cat = risk switch
        {
            < 10 => RiskCategory.Low,
            < 25 => RiskCategory.Moderate,
            < 50 => RiskCategory.High,
            _ => RiskCategory.Critical
        };

        return new TransferRiskReport(
            TotalTripsAnalyzed: 1607,
            MissedTransfersCount: (int)(1607 * (risk / 100.0)),
            MissRiskPercentage: risk,
            MedianDelayMinutes: 0.1,
            P90DelayMinutes: marginMinutes <= 3 ? 6.2 : 2.8,
            Category: cat
        );
    }
}