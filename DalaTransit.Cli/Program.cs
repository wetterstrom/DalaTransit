using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using DalaTransit.Application.UseCases;
using DalaTransit.Domain;
using DalaTransit.Infrastructure.Configuration;
using DalaTransit.Infrastructure.External;
using DalaTransit.Infrastructure.Persistence;
using DalaTransit.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;


// 1. Läs in API-nycklar (Miljövariabler i GitHub Actions prioriteras, annars secrets.json lokalt)
string? apiKey = Environment.GetEnvironmentVariable("ResRobotApiKey")
              ?? Environment.GetEnvironmentVariable("ApiKey");

string? staticApiKey = Environment.GetEnvironmentVariable("TRAFIKLAB_STATIC_API_KEY")
                    ?? Environment.GetEnvironmentVariable("StaticApiKey");

// Leta efter secrets.json på de tre vanliga platserna
var secretsPath = new[]
{
    "secrets.json",
    Path.Combine("DalaTransit.Cli", "secrets.json"),
    Path.Combine(AppContext.BaseDirectory, "secrets.json")
}.FirstOrDefault(File.Exists);

if (!string.IsNullOrEmpty(secretsPath))
{
    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(secretsPath));
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            if (doc.RootElement.TryGetProperty("ResRobotApiKey", out var k1))
                apiKey = k1.GetString();
            else if (doc.RootElement.TryGetProperty("ApiKey", out var k2))
                apiKey = k2.GetString();
        }

        if (string.IsNullOrWhiteSpace(staticApiKey))
        {
            if (doc.RootElement.TryGetProperty("StaticApiKey", out var sk))
                staticApiKey = sk.GetString();
        }
    }
    catch { }
}

staticApiKey ??= "";

// Kontrollera att ResRobot-nyckeln finns
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("Fel: Ingen API-nyckel hittades!");
    Console.WriteLine("Lägg till 'ResRobotApiKey' i secrets.json eller i GitHub Secrets.");
    Console.ResetColor();
    return;
}

// 2. Export-läge för GitHub Actions / schemalagda jobb
if (args.Contains("--export"))
{
    Console.WriteLine("=== DalaTransit Statistics Exporter ===");

    var targetPath = Path.Combine(Directory.GetCurrentDirectory(), "DalaTransit", "wwwroot", "data", "punctuality.json");
    if (!Directory.Exists(Path.GetDirectoryName(targetPath)))
    {
        targetPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "DalaTransit", "wwwroot", "data", "punctuality.json");
    }

    using var http = new HttpClient();
    var exporter = new DalaTransit.Cli.PunctualityExporter(http, apiKey);
    await exporter.RunExportAsync(Path.GetFullPath(targetPath));

    Console.WriteLine("Klart!");
    return; // Avslutar här så inte den vanliga menyn startar i Actions-jobbet
}

Console.WriteLine("==========================================================");
Console.WriteLine(" DALATRANSIT BACKGROUND COLLECTOR & EXPORTER");
Console.WriteLine(" Tryck Ctrl + C för att avsluta mjukt.");
Console.WriteLine("==========================================================\n");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (sender, eventArgs) =>
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("\n[Stoppsignal mottagen] Avslutar efter pågående cykel...");
    Console.ResetColor();
    eventArgs.Cancel = true;
    cts.Cancel();
};

var dbOptions = new DbContextOptionsBuilder<TransitDbContext>()
    .UseSqlite("Data Source=transit.db")
    .Options;

using var dbContext = new TransitDbContext(dbOptions);
await dbContext.Database.EnsureCreatedAsync();


// staticApiKey är redan färdiginläst från toppen!
var options = Options.Create(new TrafiklabOptions
{
    ApiKey = apiKey,
    StaticApiKey = staticApiKey,
    OperatorCode = "dt"
});

var handler = new HttpClientHandler
{
    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
};

using var httpClient = new HttpClient(handler);
httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DalaTransit-Collector/1.0");

var repository = new SqliteArrivalObservationRepository(dbContext);
var stopService = new SqliteStopLookupService(dbContext, httpClient, options);
var client = new TrafiklabGtfsRtClient(httpClient, options);
var riskUseCase = new AnalyzeTransferRiskUseCase(repository);

// Bestäm sökvägen till Blazor-projektets wwwroot/data-mapp
var baseDir = Directory.GetCurrentDirectory();
var wwwrootDataDir = Directory.Exists(Path.Combine(baseDir, "DalaTransit", "wwwroot"))
    ? Path.Combine(baseDir, "DalaTransit", "wwwroot", "data")
    : Path.Combine(baseDir, "..", "DalaTransit", "wwwroot", "data");

Directory.CreateDirectory(wwwrootDataDir);
var exportFilePath = Path.Combine(wwwrootDataDir, "risk-reports.json");

// 1. Kör en initial export direkt på befintlig databas så Blazor har färsk data direkt
await ExportRiskReportsAsync(dbContext, riskUseCase, exportFilePath, cts.Token);

// 2. Starta PeriodicTimer (60 sekunder)
var pollInterval = TimeSpan.FromSeconds(60);
using var timer = new PeriodicTimer(pollInterval);
int cycleNumber = 1;

try
{
    do
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"[{timestamp}] Cykel #{cycleNumber}: Hämtar live-feed... ");
        Console.ResetColor();

        try
        {
            var feedResult = await client.FetchCurrentArrivalsAsync(cts.Token);

            if (feedResult.Observations.Count > 0)
            {
                var saveResult = await repository.SaveBatchAsync(feedResult.Observations, cts.Token);
                var totalStored = await dbContext.Observations.CountAsync(cts.Token);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Mottagna: {feedResult.Observations.Count,-4} | Nya: +{saveResult.Inserted,-3} | Uppdaterade: {saveResult.Updated,-3} | Totalt i DB: {totalStored}");
                Console.ResetColor();

                // Exportera de uppdaterade analyserna till JSON
                await ExportRiskReportsAsync(dbContext, riskUseCase, exportFilePath, cts.Token);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("Inga aktiva observationer.");
                Console.ResetColor();
            }
        }
        catch (OperationCanceledException)
        {
            break;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Fel: {ex.Message}");
            Console.ResetColor();
        }

        cycleNumber++;
    }
    while (await timer.WaitForNextTickAsync(cts.Token));
}
catch (OperationCanceledException)
{
}

Console.ForegroundColor = ConsoleColor.Green;
var finalTotal = await dbContext.Observations.CountAsync();
Console.WriteLine($"\n[Färdig] Avslutad. Totalt lagrade observationer: {finalTotal} st.");
Console.ResetColor();

// Lokal funktion för att beräkna och exportera alla riskrapporter
static async Task ExportRiskReportsAsync(
    TransitDbContext db,
    AnalyzeTransferRiskUseCase useCase,
    string targetPath,
    CancellationToken ct)
{
    var totalRecords = await db.Observations.CountAsync(ct);
    if (totalRecords == 0) return;

    var margins = new[] { 2, 3, 5, 10 };
    var exportData = new RiskExportData
    {
        GeneratedAt = DateTime.Now,
        TotalDatabaseRecords = totalRecords
    };

    // 1. Beräkna nätverksövergripande snitt ("Alla hållplatser")
    foreach (var margin in margins)
    {
        var globalReport = await useCase.ExecuteAsync(stopId: null, TimeSpan.FromMinutes(margin), ct);
        exportData.Summaries.Add(new StopRiskSummary
        {
            StopId = "ALL",
            StopName = "Alla hållplatser (Snitt)",
            MarginMinutes = margin,
            Report = globalReport
        });
    }

    // 2. Beräkna per unik hållplats som faktiskt har mätpunkter
    var activeStops = await db.Observations
        .Select(x => new { x.StopId, x.StopName })
        .Distinct()
        .ToListAsync(ct);

    foreach (var stop in activeStops)
    {
        foreach (var margin in margins)
        {
            var stopReport = await useCase.ExecuteAsync(stop.StopId, TimeSpan.FromMinutes(margin), ct);
            exportData.Summaries.Add(new StopRiskSummary
            {
                StopId = stop.StopId,
                StopName = stop.StopName,
                MarginMinutes = margin,
                Report = stopReport
            });
        }
    }

    var jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    await File.WriteAllTextAsync(targetPath, JsonSerializer.Serialize(exportData, jsonOptions), ct);
    Console.WriteLine($"  -> [Export] Exporterade {exportData.Summaries.Count} analyser till {Path.GetFileName(targetPath)}");
}