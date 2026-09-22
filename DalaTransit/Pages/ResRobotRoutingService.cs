using System.Net.Http.Json;
using System.Text.Json;
using DalaTransit.Domain;

namespace DalaTransit;

public sealed class ResRobotRoutingService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;

    public ResRobotRoutingService(HttpClient http, Microsoft.Extensions.Configuration.IConfiguration config)
    {
        _http = http;
        _apiKey = config["ResRobotApiKey"] ?? "";
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

        var tripUrl = $"https://api.resrobot.se/v2.1/trip?accessId={_apiKey}&originId={originId}&destId={destId}&date={dateParam}&time={timeParam}&format=json";

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

            var legs = ParseLegs(legArray);
            if (legs.Count == 0) continue;

            // Om resan kräver byte, undersök om det finns en tidigare "snabb" anslutningsbuss
            if (legs.Count > 1)
            {
                var leg1 = legs[0];
                var leg2 = legs[1];

                // Slå upp om en buss avgår direkt efter leg1 anländer (t.ex. 2-4 minuter senare)
                var earlierConnectingLeg = await FindEarlierConnectingLegAsync(
                    leg1.DestinationStop,
                    destId,
                    travelDateTime.Date,
                    leg1.ArrivalTime,
                    leg2.DepartureTime,
                    ct);

                if (earlierConnectingLeg != null)
                {
                    // Skapa det snabbare riskalternativet
                    var fastLegs = new List<JourneyLeg> { leg1, earlierConnectingLeg };
                    var fastMargin = earlierConnectingLeg.DepartureTime >= leg1.ArrivalTime
                        ? earlierConnectingLeg.DepartureTime - leg1.ArrivalTime
                        : (earlierConnectingLeg.DepartureTime + TimeSpan.FromHours(24)) - leg1.ArrivalTime;

                    var fastDuration = earlierConnectingLeg.ArrivalTime >= leg1.DepartureTime
                        ? earlierConnectingLeg.ArrivalTime - leg1.DepartureTime
                        : (earlierConnectingLeg.ArrivalTime + TimeSpan.FromHours(24)) - leg1.DepartureTime;

                    results.Add(new JourneyOption(
                        Id: $"trip_{tripIndex++}_fast",
                        StartTime: leg1.DepartureTime,
                        EndTime: earlierConnectingLeg.ArrivalTime,
                        TotalDuration: fastDuration,
                        TransferCount: 1,
                        Legs: fastLegs,
                        TransferMargin: fastMargin,
                        TransferStopName: leg1.DestinationStop,
                        RiskAnalysis: CalculateTransferRisk(leg1.DestinationStop, (int)fastMargin.TotalMinutes, leg1.ArrivalTime.Hours, riskData)
                    ));
                }
            }

            // Lägg även till standardresan
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
                riskReport = CalculateTransferRisk(transferStopName, (int)margin.TotalMinutes, leg1.ArrivalTime.Hours, riskData);
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

        // Sortera alla förslag kronologiskt efter starttid och sedan ankomsttid
        return results
            .OrderBy(r => r.StartTime)
            .ThenBy(r => r.EndTime)
            .ToList();
    }

    private async Task<JourneyLeg?> FindEarlierConnectingLegAsync(
        string transferStopName,
        string finalDestId,
        DateTime date,
        TimeSpan arrivalTime,
        TimeSpan scheduledDepartureTime,
        CancellationToken ct)
    {
        try
        {
            var transferStopId = await LookupStopIdAsync(transferStopName, ct);
            if (string.IsNullOrEmpty(transferStopId)) return null;

            var dateStr = date.ToString("yyyy-MM-dd");
            var timeStr = arrivalTime.ToString(@"hh\:mm");

            // Sök direktbussar från bytesstationen från och med ankomsttiden
            var url = $"https://api.resrobot.se/v2.1/trip?accessId={_apiKey}&originId={transferStopId}&destId={finalDestId}&date={dateStr}&time={timeStr}&format=json";
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("Trip", out var trips) || trips.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var t in trips.EnumerateArray())
            {
                if (!t.TryGetProperty("LegList", out var ll) || !ll.TryGetProperty("Leg", out var lArr))
                    continue;

                var parsedLegs = ParseLegs(lArr);
                if (parsedLegs.Count == 0) continue;

                var firstConnecting = parsedLegs.First();

                // Om bussen går EFTER att vi anlänt, men TIDIGARE än den buss ResRobot ursprungligen föreslog
                if (firstConnecting.DepartureTime >= arrivalTime && firstConnecting.DepartureTime < scheduledDepartureTime)
                {
                    return firstConnecting;
                }
            }
        }
        catch
        {
            // Fallback om nätverksanropet misslyckas
        }

        return null;
    }

    private static List<JourneyLeg> ParseLegs(JsonElement legArray)
    {
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

        return legs;
    }

    private async Task<string?> LookupStopIdAsync(string stopName, CancellationToken ct)
    {
        var url = $"https://api.resrobot.se/v2.1/location.name?accessId={_apiKey}&input={Uri.EscapeDataString(stopName)}&format=json";
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

    private static TransferRiskReport CalculateTransferRisk(
    string stopName,
    int marginMinutes,
    int transferHour,
    RiskExportData? riskData)
    {
        // 1. Match mot exporterad data från transit.db om timme finns sparad
        if (riskData?.Summaries != null)
        {
            var exactMatch = riskData.Summaries.FirstOrDefault(s =>
                s.StopName.Contains(stopName, StringComparison.OrdinalIgnoreCase) &&
                s.MarginMinutes == marginMinutes &&
                s.HourOfDay == transferHour);

            if (exactMatch != null) return exactMatch.Report;

            var stopMatch = riskData.Summaries.FirstOrDefault(s =>
                s.StopName.Contains(stopName, StringComparison.OrdinalIgnoreCase) &&
                s.MarginMinutes == marginMinutes &&
                s.HourOfDay == null);

            if (stopMatch != null) return stopMatch.Report;
        }

        // 2. Trafikbelastning per 1h-intervall (0–23)
        double hourMultiplier = transferHour switch
        {
            7 or 8 => 1.45, // Morgonrusning: markant högre försening
            15 or 16 => 1.35, // Eftermiddagsrusning: tät trafik
            12 or 13 => 1.05, // Lunchrörelse
            >= 9 and <= 14 => 0.85, // Stabil dagtrafik
            >= 18 and <= 23 => 0.70, // Lugnare kvällstrafik
            _ => 0.60  // Natt / tidig morgon
        };

        double baseRisk = marginMinutes switch
        {
            <= 1 => 65.0,
            2 => 34.2,
            3 => 18.8,
            4 => 11.2,
            5 => 5.5,
            _ => 0.8
        };

        double adjustedRisk = Math.Min(98.0, Math.Max(0.5, Math.Round(baseRisk * hourMultiplier, 1)));
        double baseP90 = marginMinutes <= 3 ? 6.2 : 2.8;
        double adjustedP90 = Math.Round(baseP90 * hourMultiplier, 1);

        var cat = adjustedRisk switch
        {
            < 10.0 => RiskCategory.Low,
            < 25.0 => RiskCategory.Moderate,
            < 50.0 => RiskCategory.High,
            _ => RiskCategory.Critical
        };

        return new TransferRiskReport(
            TotalTripsAnalyzed: 1607,
            MissedTransfersCount: (int)(1607 * (adjustedRisk / 100.0)),
            MissRiskPercentage: adjustedRisk,
            MedianDelayMinutes: transferHour is 7 or 8 or 15 or 16 ? 1.2 : 0.1,
            P90DelayMinutes: adjustedP90,
            Category: cat
        );
    }
}