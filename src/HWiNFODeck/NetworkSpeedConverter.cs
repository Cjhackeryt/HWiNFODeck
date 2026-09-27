namespace HWiNFODeck;

internal static class NetworkSpeedConverter
{
    public static bool IsDataRateUnit(string? unit) => unit?.ToLowerInvariant() switch
    {
        "b/s" or "kb/s" or "kbyte/s" or "mb/s" or "mbyte/s" or "gb/s" or "gbyte/s"
            or "kbps" or "mbps" or "gbps" => true,
        _ => false
    };
    public static double? ToMegabytesPerSecond(HWiNFOSensor? reading)
    {
        if (reading is null)
            return null;

        return reading.Unit.ToLowerInvariant() switch
        {
            "b/s" => reading.Value / 1024 / 1024,
            "kb/s" or "kbyte/s" => reading.Value / 1024,
            "mb/s" or "mbyte/s" => reading.Value,
            "gb/s" or "gbyte/s" => reading.Value * 1024,
            "kbps" => reading.Value / 1000 / 8,
            "mbps" => reading.Value / 8,
            "gbps" => reading.Value / 8 * 1024,
            _ => null
        };
    }

    public static double? ToMegabitsPerSecond(HWiNFOSensor? reading) =>
        ToMegabytesPerSecond(reading) is { } megabytes ? megabytes * 8 : null;
}