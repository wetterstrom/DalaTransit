using System;
using System.Collections.Generic;
using System.Text;

namespace DalaTransit.Infrastructure.Configuration;

public class TrafiklabOptions
{
    /// <summary>
    /// API-nyckel för GTFS Regional Realtime (GTFS-RT)
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// API-nyckel för GTFS Regional Static data (stops.txt, tidtabeller)
    /// </summary>
    public string StaticApiKey { get; set; } = string.Empty;

    public string OperatorCode { get; set; } = "dt";

    public string FeedUrlTemplate =>
        $"https://opendata.samtrafiken.se/gtfs-rt/{OperatorCode}/TripUpdates.pb?key={ApiKey}";

    public string StaticFeedUrlTemplate =>
        $"https://opendata.samtrafiken.se/gtfs/{OperatorCode}/{OperatorCode}.zip?key={StaticApiKey}";
}