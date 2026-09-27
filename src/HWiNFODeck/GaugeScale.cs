using System.Globalization;
using System.Text.Json;
using MacroDeck.Sdk.Variables;

namespace HWiNFODeck;

/// <summary>
/// The stored data shape both gauge-style widgets read and write.
/// </summary>
internal sealed record GaugeData(string Variable, double Max, string Unit);

/// <summary>
/// The contract shared by the gauge-style widgets: the stored data, the variable lookup behind it
/// and the formatting of a reading. The rendering belongs to each widget.
/// </summary>
internal static class GaugeScale
{
    public const string PluginId = "com.hwinfo.cjhackeryt";
    public const double DefaultMax = 100;
    public const double DefaultRefreshSeconds = 1;
    public const double MinRefreshSeconds = 0.25;
    public const double MaxRefreshSeconds = 60;

    /// <summary>
    /// The form the host hands back in a surface attribute. The host owns the qualified id, so this
    /// only exists to recognise it on the way in and to pin it in tests.
    /// </summary>
    public static string QualifiedId(string localId) => PluginId + "::" + localId;

    public static GaugeData ParseData(JsonElement data)
    {
        var isObject = data.ValueKind == JsonValueKind.Object;
        var variable = isObject &&
            data.TryGetProperty("variable", out var variableElement) &&
            variableElement.ValueKind == JsonValueKind.String
                ? variableElement.GetString() ?? ""
                : "";
        var max = isObject &&
            data.TryGetProperty("max", out var maxElement) &&
            maxElement.ValueKind == JsonValueKind.Number &&
            maxElement.TryGetDouble(out var parsedMax)
                ? parsedMax
                : DefaultMax;
        var unit = isObject &&
            data.TryGetProperty("unit", out var unitElement) &&
            unitElement.ValueKind == JsonValueKind.String
                ? unitElement.GetString() ?? ""
                : "";
        return new GaugeData(variable, max, unit);
    }

    public static double CoerceRefreshSeconds(double configured) =>
        double.IsFinite(configured)
            ? Math.Clamp(configured, MinRefreshSeconds, MaxRefreshSeconds)
            : DefaultRefreshSeconds;

    public static double ToRatio(double value, double max)
    {
        if (max <= 0 || !double.IsFinite(value))
            return 0;
        return Math.Clamp(value / max, 0, 1);
    }

    public static double? ToNumber(object? value) => value switch
    {
        double number when double.IsFinite(number) => number,
        float number when double.IsFinite(number) => number,
        int number => number,
        long number => number,
        decimal number => (double)number,
        string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            && double.IsFinite(parsed) => parsed,
        bool flag => flag ? 1 : 0,
        _ => null
    };

    public static double? ResolveValue(IReadOnlyList<VariableHandle> handles, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;

        var local = reference.Contains("::", StringComparison.Ordinal)
            ? reference[(reference.LastIndexOf(':') + 1)..]
            : reference;
        var handle = handles.FirstOrDefault(x =>
                string.Equals(x.Name, reference, StringComparison.OrdinalIgnoreCase)) ??
            handles.FirstOrDefault(x =>
                string.Equals(x.DefinitionId, local, StringComparison.OrdinalIgnoreCase)) ??
            handles.FirstOrDefault(x =>
                string.Equals(x.Name, local, StringComparison.OrdinalIgnoreCase));
        return handle is null ? null : ToNumber(handle.Value);
    }

    public static string? OwnLocalId(IReadOnlyList<VariableDefinition> definitions, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;

        var local = reference;
        var qualifier = PluginId + "::";
        if (local.StartsWith(qualifier, StringComparison.OrdinalIgnoreCase))
            local = local[qualifier.Length..];
        else if (local.Contains("::", StringComparison.Ordinal))
            return null;

        return definitions
            .FirstOrDefault(x =>
                string.Equals(x.Name, local, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Id, local, StringComparison.OrdinalIgnoreCase))
            ?.Id;
    }

    public static string FormatMax(double max) =>
        max % 1 == 0
            ? ((long)max).ToString(CultureInfo.InvariantCulture)
            : max.ToString("F1", CultureInfo.InvariantCulture);

    /// <summary>
    /// The number alone, because the gauge draws the unit on its own line under it.
    /// </summary>
    public static string FormatValue(double value, bool hasValue) =>
        hasValue ? value.ToString("F1", CultureInfo.InvariantCulture) : "–";
}
