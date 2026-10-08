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

    private static readonly string[] GraphicsSensorHints =
    [
        "gpu", "graphics", "geforce", "radeon", "nvidia", "intel arc"
    ];

    public static HWiNFOSensor? FindGpuUsage(IReadOnlyList<HWiNFOSensor> readings) =>
        FindGraphicsReading(
            readings,
            x => IsPercent(x.Unit) &&
                !ContainsAny(x.ReadingName, ["memory", "vram"]) &&
                ContainsAny(x.ReadingName, ["usage", "load", "utilization", "utilisation"]));

    public static HWiNFOSensor? FindGpuTemperature(IReadOnlyList<HWiNFOSensor> readings) =>
        FindGraphicsReading(
            readings,
            x => x.Unit.Contains('C', StringComparison.OrdinalIgnoreCase) &&
                ContainsAny(x.ReadingName, ["temperature", "temp"]));

    public static HWiNFOSensor? FindGpuClock(IReadOnlyList<HWiNFOSensor> readings) =>
        FindGraphicsReading(
            readings,
            x => IsFrequency(x.Unit) && x.ReadingName.Contains("clock", StringComparison.OrdinalIgnoreCase));

    public static HWiNFOSensor? FindGpuPower(IReadOnlyList<HWiNFOSensor> readings) =>
        FindGraphicsReading(
            readings,
            x => IsPower(x.Unit) && x.ReadingName.Contains("power", StringComparison.OrdinalIgnoreCase));

    public static HWiNFOSensor? FindVramUsagePercent(IReadOnlyList<HWiNFOSensor> readings) =>
        FindGraphicsReading(
            readings,
            x => IsPercent(x.Unit) &&
                ContainsAny(x.ReadingName, ["memory", "vram"]) &&
                ContainsAny(x.ReadingName, ["usage", "used", "load"]));

    public static double? FindVramUsedMegabytes(IReadOnlyList<HWiNFOSensor> readings)
    {
        var reading = FindGraphicsReading(
            readings,
            x => IsMemoryUnit(x.Unit) &&
                ContainsAny(x.ReadingName, ["memory", "vram"]) &&
                ContainsAny(x.ReadingName, ["used", "usage", "allocated", "dedicated"]));

        return reading is null ? null : ToMegabytes(reading.Value, reading.Unit);
    }

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

    private static HWiNFOSensor? FindGraphicsReading(
        IReadOnlyList<HWiNFOSensor> readings,
        Func<HWiNFOSensor, bool> accept) =>
        readings.FirstOrDefault(x =>
            ContainsAny(x.SensorName, GraphicsSensorHints) &&
            accept(x));

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

    private static bool IsPercent(string unit) =>
        unit.Equals("%", StringComparison.OrdinalIgnoreCase) ||
        unit.Equals("percent", StringComparison.OrdinalIgnoreCase);

    private static bool IsFrequency(string unit) =>
        unit.Equals("MHz", StringComparison.OrdinalIgnoreCase) ||
        unit.Equals("GHz", StringComparison.OrdinalIgnoreCase);

    private static bool IsPower(string unit) =>
        unit.Equals("W", StringComparison.OrdinalIgnoreCase) ||
        unit.Equals("mW", StringComparison.OrdinalIgnoreCase);

    private static bool IsMemoryUnit(string unit) =>
        unit.Equals("GB", StringComparison.OrdinalIgnoreCase) ||
        unit.Equals("MB", StringComparison.OrdinalIgnoreCase) ||
        unit.Equals("KB", StringComparison.OrdinalIgnoreCase) ||
        unit.Equals("TB", StringComparison.OrdinalIgnoreCase);

    private static double ToMegabytes(double value, string unit) =>
        unit.ToLowerInvariant() switch
        {
            "tb" => value * 1024 * 1024,
            "gb" => value * 1024,
            "kb" => value / 1024,
            _ => value
        };

    private static bool ContainsAny(string text, string[] words) =>
        words.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
}