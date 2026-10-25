using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

// --------------------------------------------------
// USGS Geomagnetic Data Collector
// Retrieves, organizes, and stores observatory data.
// --------------------------------------------------

Console.WriteLine("\n\nUSGS Geomagnetic Data Collector");
Console.WriteLine("-------------------------------");

// Collect the same period from all three stations.
string[] stations = { "SJG", "BOU", "BRW" };

// Get the most recent completed UTC hour.
DateTime nowUtc = DateTime.UtcNow;

DateTime endUtc = new(
    nowUtc.Year,
    nowUtc.Month,
    nowUtc.Day,
    nowUtc.Hour,
    0,
    0,
    DateTimeKind.Utc);

DateTime startUtc = endUtc.AddHours(-1);

string startTime =
    startUtc.ToString(
        "yyyy-MM-ddTHH:mm:ssZ",
        CultureInfo.InvariantCulture);

string endTime =
    endUtc.ToString(
        "yyyy-MM-ddTHH:mm:ssZ",
        CultureInfo.InvariantCulture);

Console.WriteLine(
    $"Collection period: {startTime} to {endTime}");

JsonSerializerOptions options = new()
{
    PropertyNameCaseInsensitive = true
};

using HttpClient client = new();


// --------------------------------------------------
// Initialize the SQLite database.
// --------------------------------------------------

using SqliteConnection db =
    new("Data Source=geomag.db");

db.Open();

using (SqliteCommand command = db.CreateCommand())
{
    command.CommandText = """
        CREATE TABLE IF NOT EXISTS GeomagReadings
        (
            StationCode TEXT NOT NULL,
            TimestampUtc TEXT NOT NULL,
            DataType TEXT NOT NULL,
            X REAL,
            Y REAL,
            Z REAL,
            F REAL,

            PRIMARY KEY
            (
                StationCode,
                TimestampUtc,
                DataType
            )
        );
        """;

    command.ExecuteNonQuery();
}

Console.WriteLine("SQLite database initialized.");


// --------------------------------------------------
// Request and process each observatory.
// --------------------------------------------------

foreach (string station in stations)
{
    Console.WriteLine();
    Console.WriteLine($"Retrieving station: {station}");

    string url =
        "https://geomag.usgs.gov/ws/data/" +
        $"?id={station}" +
        $"&starttime={startTime}" +
        $"&endtime={endTime}" +
        "&elements=X,Y,Z,F" +
        "&sampling_period=60" +
        "&type=adjusted" +
        "&format=json";

    try
    {
        using HttpResponseMessage response =
            await client.GetAsync(url);

        Console.WriteLine(
            $"HTTP Status: {(int)response.StatusCode} " +
            response.StatusCode);

        response.EnsureSuccessStatusCode();

        string json =
            await response.Content.ReadAsStringAsync();

        UsgsResponse data =
            JsonSerializer.Deserialize<UsgsResponse>(
                json, options)
            ?? throw new InvalidDataException(
                "Unable to parse USGS response.");

        // Confirm which station the API returned.
        string returnedStation =
            data.Metadata.Intermagnet.Imo.IagaCode;

        if (!string.Equals(
            returnedStation,
            station,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The returned station does not match " +
                "the requested station.");
        }

        Console.WriteLine(
            $"Station: {data.Metadata.Intermagnet.Imo.Name}");

        Console.WriteLine(
            $"Data Type: {data.Metadata.Intermagnet.DataType}");

        // Convert the parallel arrays into reading objects.
        List<GeomagReading> readings =
            ConvertToReadings(data, station);

        Console.WriteLine(
            $"Readings organized: {readings.Count}");

        Console.WriteLine();
        Console.WriteLine(
            "Timestamp                  X          Y          Z          F");

        Console.WriteLine(
            "------------------------------------------------------------------");

        // Display the first five readings.
        foreach (GeomagReading reading in readings.Take(5))
        {
            Console.WriteLine(
                $"{reading.TimestampUtc:yyyy-MM-dd HH:mm}  " +
                $"{FormatValue(reading.X),10} " +
                $"{FormatValue(reading.Y),10} " +
                $"{FormatValue(reading.Z),10} " +
                $"{FormatValue(reading.F),10}");
        }

        // Count timestamps containing any missing measurement.
        int missingCount = readings.Count(r =>
            r.X == null ||
            r.Y == null ||
            r.Z == null ||
            r.F == null);

        Console.WriteLine(
            $"Readings with missing values: {missingCount}");

        // Save measurements to SQLite.
        int inserted = SaveReadings(db, readings);

        Console.WriteLine(
            $"New database records inserted: {inserted}");
    }
    catch (Exception ex)
    {
        Console.WriteLine(
            $"Error processing {station}: {ex.Message}");
    }
}


// --------------------------------------------------
// Verify total database records.
// --------------------------------------------------

using (SqliteCommand command = db.CreateCommand())
{
    command.CommandText =
        "SELECT COUNT(*) FROM GeomagReadings;";

    long total = (long)command.ExecuteScalar()!;

    Console.WriteLine();
    Console.WriteLine($"Total database records: {total}");
}

Console.WriteLine("Collection complete.");


// --------------------------------------------------
// Convert USGS arrays into individual readings.
// --------------------------------------------------

static List<GeomagReading> ConvertToReadings(
    UsgsResponse data,
    string stationCode)
{
    string[] required = { "X", "Y", "Z", "F" };

    // Locate each measurement series by ID.
    Dictionary<string, ElementSeries> series =
        new(StringComparer.OrdinalIgnoreCase);

    foreach (string element in required)
    {
        ElementSeries found =
            data.Values.FirstOrDefault(v =>
                string.Equals(
                    v.Id,
                    element,
                    StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException(
                $"Missing measurement series: {element}");

        // Ensure every series matches the timestamps array.
        if (found.Values.Count != data.Times.Count)
        {
            throw new InvalidDataException(
                $"Unexpected number of {element} values.");
        }

        series.Add(element, found);
    }

    List<GeomagReading> readings = new();

    for (int i = 0; i < data.Times.Count; i++)
    {
        GeomagReading reading = new()
        {
            StationCode = stationCode,
            TimestampUtc = data.Times[i],
            DataType = data.Metadata.Intermagnet.DataType,

            X = series["X"].Values[i],
            Y = series["Y"].Values[i],
            Z = series["Z"].Values[i],
            F = series["F"].Values[i]
        };

        readings.Add(reading);
    }

    return readings;
}


// --------------------------------------------------
// Save readings using a SQLite transaction.
// --------------------------------------------------

static int SaveReadings(
    SqliteConnection db,
    List<GeomagReading> readings)
{
    using SqliteTransaction transaction =
        db.BeginTransaction();

    using SqliteCommand command = db.CreateCommand();

    command.Transaction = transaction;

    command.CommandText = """
        INSERT OR IGNORE INTO GeomagReadings
        (
            StationCode,
            TimestampUtc,
            DataType,
            X, Y, Z, F
        )
        VALUES
        (
            $station,
            $timestamp,
            $type,
            $x, $y, $z, $f
        );
        """;

    // Create the parameters once and reuse them.
    var pStation =
        command.Parameters.Add("$station", SqliteType.Text);

    var pTimestamp =
        command.Parameters.Add("$timestamp", SqliteType.Text);

    var pType =
        command.Parameters.Add("$type", SqliteType.Text);

    var pX = command.Parameters.Add("$x", SqliteType.Real);
    var pY = command.Parameters.Add("$y", SqliteType.Real);
    var pZ = command.Parameters.Add("$z", SqliteType.Real);
    var pF = command.Parameters.Add("$f", SqliteType.Real);

    int inserted = 0;

    foreach (GeomagReading reading in readings)
    {
        pStation.Value = reading.StationCode;

        pTimestamp.Value =
            reading.TimestampUtc.ToUniversalTime()
                .ToString(
                    "yyyy-MM-ddTHH:mm:ssZ",
                    CultureInfo.InvariantCulture);

        pType.Value = reading.DataType;

        // Preserve missing readings as database NULL.
        pX.Value = (object?)reading.X ?? DBNull.Value;
        pY.Value = (object?)reading.Y ?? DBNull.Value;
        pZ.Value = (object?)reading.Z ?? DBNull.Value;
        pF.Value = (object?)reading.F ?? DBNull.Value;

        inserted += command.ExecuteNonQuery();
    }

    transaction.Commit();

    return inserted;
}


// --------------------------------------------------
// Format nullable values for console display.
// --------------------------------------------------

static string FormatValue(double? value)
{
    return value?.ToString(
        "F3",
        CultureInfo.InvariantCulture) ?? "N/A";
}


// -------------------------
// USGS Response Classes
// -------------------------

public class UsgsResponse
{
    public string Type { get; set; } = "";

    public UsgsMetadata Metadata { get; set; } = new();

    public List<DateTime> Times { get; set; } = new();

    public List<ElementSeries> Values { get; set; } = new();
}

public class UsgsMetadata
{
    public IntermagnetMetadata Intermagnet { get; set; } = new();

    public int Status { get; set; }

    public DateTime Generated { get; set; }
}

public class IntermagnetMetadata
{
    public ImoMetadata Imo { get; set; } = new();

    [JsonPropertyName("reported_orientation")]
    public string ReportedOrientation { get; set; } = "";

    [JsonPropertyName("sensor_orientation")]
    public string SensorOrientation { get; set; } = "";

    [JsonPropertyName("data_type")]
    public string DataType { get; set; } = "";

    [JsonPropertyName("sampling_period")]
    public double SamplingPeriod { get; set; }

    [JsonPropertyName("digital_sampling_rate")]
    public double DigitalSamplingRate { get; set; }
}

public class ImoMetadata
{
    [JsonPropertyName("iaga_code")]
    public string IagaCode { get; set; } = "";

    public string Name { get; set; } = "";

    public List<double> Coordinates { get; set; } = new();
}

public class ElementSeries
{
    public string Id { get; set; } = "";

    public List<double?> Values { get; set; } = new();
}

public class GeomagReading
{
    public string StationCode { get; set; } = "";

    public DateTime TimestampUtc { get; set; }

    public string DataType { get; set; } = "";

    public double? X { get; set; }

    public double? Y { get; set; }

    public double? Z { get; set; }

    public double? F { get; set; }
}