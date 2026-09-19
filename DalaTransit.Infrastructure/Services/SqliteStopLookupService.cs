using DalaTransit.Application.Interfaces;
using DalaTransit.Infrastructure.Configuration;
using DalaTransit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.IO.Compression;

namespace DalaTransit.Infrastructure.Services;

public sealed class SqliteStopLookupService : IStopLookupService
{
    private readonly TransitDbContext _context;
    private readonly HttpClient _httpClient;
    private readonly TrafiklabOptions _options;
    private readonly ConcurrentDictionary<string, string> _cache = new();
    private bool _cacheLoaded = false;

    public SqliteStopLookupService(
        TransitDbContext context,
        HttpClient httpClient,
        IOptions<TrafiklabOptions> options)
    {
        _context = context;
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<string> GetStopNameAsync(string stopId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stopId)) return "Okänd hållplats";

        // Läs in hela registret till minnet vid första anropet för maximal prestanda
        if (!_cacheLoaded)
        {
            var dbStops = await _context.Stops.AsNoTracking().ToListAsync(cancellationToken);
            foreach (var stop in dbStops)
            {
                _cache[stop.StopId] = stop.StopName;
            }
            _cacheLoaded = true;
        }

        if (_cache.TryGetValue(stopId, out var cachedName))
        {
            return cachedName;
        }

        return $"Hållplats {stopId}";
    }

    public async Task SyncStopsFromTrafiklabAsync(CancellationToken cancellationToken = default)
    {
        // Trafiklabs statiska GTFS-URL för Dalatrafik (dt)
        var staticUrl = !string.IsNullOrWhiteSpace(_options.StaticApiKey)
             ? _options.StaticFeedUrlTemplate
             : $"https://opendata.samtrafiken.se/gtfs/{_options.OperatorCode}/{_options.OperatorCode}.zip?key={_options.ApiKey}";

        using var response = await _httpClient.GetAsync(staticUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var zipStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        var stopsEntry = archive.GetEntry("stops.txt");
        if (stopsEntry == null)
            throw new InvalidOperationException("stops.txt saknas i Dalatrafiks GTFS-arkiv.");

        await using var entryStream = stopsEntry.Open();
        using var reader = new StreamReader(entryStream);

        var headerLine = await reader.ReadLineAsync(cancellationToken);
        if (headerLine == null) return;

        var headers = ParseCsvLine(headerLine);
        int idIndex = Array.IndexOf(headers, "stop_id");
        int nameIndex = Array.IndexOf(headers, "stop_name");

        if (idIndex == -1 || nameIndex == -1)
            throw new InvalidOperationException("CSV-strukturen i stops.txt saknar obligatoriska kolumner.");

        var existingIds = await _context.Stops.Select(s => s.StopId).ToHashSetAsync(cancellationToken);
        var newStops = new List<StopRecord>();

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var parts = ParseCsvLine(line);
            if (parts.Length <= Math.Max(idIndex, nameIndex)) continue;

            var stopId = parts[idIndex].Trim();
            var stopName = parts[nameIndex].Trim();

            if (!existingIds.Contains(stopId))
            {
                newStops.Add(new StopRecord { StopId = stopId, StopName = stopName });
                existingIds.Add(stopId);
                _cache[stopId] = stopName;
            }
        }

        if (newStops.Count > 0)
        {
            await _context.Stops.AddRangeAsync(newStops, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var current = new System.Text.StringBuilder();

        foreach (char c in line)
        {
            if (c == '\"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        result.Add(current.ToString());
        return result.ToArray();
    }
}