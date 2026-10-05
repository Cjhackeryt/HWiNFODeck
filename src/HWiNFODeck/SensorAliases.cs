namespace HWiNFODeck;

internal static class SensorAliases
{
    private static readonly string[] DownloadPhrases =
    [
        "Download Speed", "Download Rate", "Down. Speed", "Down Speed",
        "Current DL Rate", "DL Rate", "DL Speed", "Current Download"
    ];

    private static readonly string[] UploadPhrases =
    [
        "Upload Speed", "Upload Rate", "Up. Speed", "Up Speed",
        "Current UL Rate", "UL Rate", "UL Speed", "Current Upload"
    ];

    private static readonly string[] DownloadWords = ["download", "down", "dl", "receive", "rx"];
    private static readonly string[] UploadWords = ["upload", "up", "ul", "transmit", "tx", "send", "out"];

    private static readonly string[] NetworkSensorHints =
    [
        "net", "ethernet", "wifi", "wi-fi", "wlan", "lan",
        "wireless", "gbe", "gigabit", "network", "killer"
    ];

    private static readonly string[] DriveSensorHints =
    [
        "drive", "disk", "ssd", "hdd", "nvme", "sata", "smart", "s.m.a.r.t", "storage"
    ];

    private static readonly string[] DriveUsedPhrases =
    [
        "Drive Used %", "Drive Used", "Disk Used", "Used %", "Used Space", "Used"
    ];

    private static readonly string[] DriveFreePhrases =
    [
        "Drive Free Space", "Free Space", "Available Space", "Free", "Available"
    ];

    // HWiNFO names every graphics sensor "GPU [#n]: <model>". GPU labels are matched exactly rather than
    // word by word: "GPU Temperature" is also contained in "GPU Memory Junction Temperature".
    private const string GpuSensorPrefix = "GPU";

    public static readonly string[] GpuUsageLabels = ["GPU Core Load", "GPU Utilization", "GPU D3D Usage"];
    public static readonly string[] GpuTemperatureLabels = ["GPU Temperature"];
    public static readonly string[] GpuHotSpotTemperatureLabels = ["GPU Hot Spot Temperature"];
    public static readonly string[] GpuMemoryJunctionTemperatureLabels = ["GPU Memory Junction Temperature"];
    public static readonly string[] GpuMemoryUsageLabels = ["GPU Memory Usage"];
    public static readonly string[] GpuClockLabels = ["GPU Clock"];
    public static readonly string[] GpuEffectiveClockLabels = ["GPU Effective Clock"];
    public static readonly string[] GpuPowerLabels = ["GPU Power", "GPU ASIC Power", "Total Board Power"];
    public static readonly string[] GpuFanLabels = ["GPU Fan1", "GPU Fan"];

    public static HWiNFOSensor? FindDownloadReading(IReadOnlyList<HWiNFOSensor> readings) =>
        FindNetworkReading(readings, DownloadPhrases, DownloadWords);

    public static HWiNFOSensor? FindUploadReading(IReadOnlyList<HWiNFOSensor> readings) =>
        FindNetworkReading(readings, UploadPhrases, UploadWords);

    public static double? FindDriveUsedPercent(IReadOnlyList<HWiNFOSensor> readings)
    {
        var reading = FindDriveReading(
            readings,
            DriveUsedPhrases,
            x => x.Unit.ToLowerInvariant() is "%" or "percent");
        return reading?.Value;
    }

    public static double? FindDriveFreeGigabytes(IReadOnlyList<HWiNFOSensor> readings)
    {
        var reading = FindDriveReading(
            readings,
            DriveFreePhrases,
            x => x.Unit.ToLowerInvariant() is "gb" or "gbyte" or "gbytes"
                or "tb" or "tbyte" or "tbytes" or "mb" or "mbyte" or "mbytes"
                or "kb" or "kbyte" or "kbytes");
        if (reading is null)
            return null;

        return reading.Unit.ToLowerInvariant() switch
        {
            "gb" or "gbyte" or "gbytes" => reading.Value,
            "tb" or "tbyte" or "tbytes" => reading.Value * 1024,
            "mb" or "mbyte" or "mbytes" => reading.Value / 1024,
            _ => reading.Value / 1024 / 1024
        };
    }

    public static (double UsedPercent, double FreeGigabytes, string Name)? ReadFixedDrive()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed || drive.TotalSize <= 0)
                    continue;

                var freeGigabytes = (double)drive.AvailableFreeSpace / 1024 / 1024 / 1024;
                var usedPercent = 100 * (1 - (double)drive.AvailableFreeSpace / drive.TotalSize);
                return (usedPercent, freeGigabytes, drive.Name);
            }
            catch (Exception)
            {
                continue;
            }
        }

        return null;
    }

    /// <summary>
    /// The reading of the first GPU sensor whose label is one of <paramref name="labels"/>, tried in
    /// order. <paramref name="unitOk"/> tells apart readings HWiNFO publishes twice under one label, such
    /// as a fan's RPM and its duty cycle in percent.
    /// </summary>
    public static HWiNFOSensor? FindGpuReading(
        IReadOnlyList<HWiNFOSensor> readings,
        string[] labels,
        Func<string, bool>? unitOk = null)
    {
        var gpuReadings = readings.Where(x => IsGpuSensor(x.SensorName)).ToArray();
        foreach (var label in labels)
        {
            var match = gpuReadings.FirstOrDefault(x =>
                x.ReadingName.Equals(label, StringComparison.OrdinalIgnoreCase) &&
                (unitOk is null || unitOk(x.Unit)));
            if (match is not null)
                return match;
        }

        return null;
    }

    public static string? FindGpuName(IReadOnlyList<HWiNFOSensor> readings) =>
        readings.FirstOrDefault(x => IsGpuSensor(x.SensorName))?.SensorName;

    public static bool IsTemperatureUnit(string unit) => unit.Contains('C', StringComparison.OrdinalIgnoreCase);

    public static bool IsPercentUnit(string unit) => unit.Trim() == "%";

    public static bool IsRpmUnit(string unit) => unit.Trim().Equals("RPM", StringComparison.OrdinalIgnoreCase);

    private static bool IsGpuSensor(string sensorName) =>
        sensorName.StartsWith(GpuSensorPrefix, StringComparison.OrdinalIgnoreCase);

    private static HWiNFOSensor? FindNetworkReading(
        IReadOnlyList<HWiNFOSensor> readings,
        string[] phrases,
        string[] directionWords)
    {
        var rates = readings.Where(x => NetworkSpeedConverter.IsDataRateUnit(x.Unit)).ToArray();
        return MatchPhrase(rates, phrases)
            ?? MatchWords(rates, directionWords)
            ?? MatchWords(rates, NetworkSensorHints);
    }

    private static HWiNFOSensor? FindDriveReading(
        IReadOnlyList<HWiNFOSensor> readings,
        string[] phrases,
        Func<HWiNFOSensor, bool> unitOk) =>
        MatchPhrase(
            readings.Where(x => ContainsAny(x.SensorName, DriveSensorHints)).ToArray(),
            phrases,
            unitOk);

    private static HWiNFOSensor? MatchPhrase(
        IReadOnlyList<HWiNFOSensor> readings,
        string[] phrases,
        Func<HWiNFOSensor, bool>? accept = null)
    {
        foreach (var phrase in phrases)
        {
            var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var match = readings.FirstOrDefault(x =>
                (accept is null || accept(x)) &&
                words.All(word => $"{x.SensorName} {x.ReadingName}".Contains(word, StringComparison.OrdinalIgnoreCase)));
            if (match is not null)
                return match;
        }

        return null;
    }

    private static HWiNFOSensor? MatchWords(IReadOnlyList<HWiNFOSensor> readings, string[] words) =>
        readings.FirstOrDefault(x => ContainsAny($"{x.SensorName} {x.ReadingName}", words));

    private static bool ContainsAny(string text, string[] words) =>
        words.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
}