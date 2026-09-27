using MacroDeck.Sdk.Variables;
using Serilog;

namespace HWiNFODeck;

public sealed class HWiNFOVariableProvider : IVariableProvider
{
    private static readonly string[] TextVariableIds =
    [
        "hwinfo_status",
        "hwinfo_version",
        "hwinfo_cpu_name",
        "macrodeck_version",
        "macrodeck_sdk_version"
    ];

    private static readonly string[] NumericVariableIds =
    [
        "hwinfo_sensor_count",
        "hwinfo_cpu_usage",
        "hwinfo_cpu_temperature",
        "hwinfo_cpu_package_temperature",
        "hwinfo_cpu_clock",
        "hwinfo_cpu_effective_clock",
        "hwinfo_cpu_power",
        "hwinfo_cpu_voltage",
        "hwinfo_cpu_fan_rpm",
        "hwinfo_network_download_speed_mbps",
        "hwinfo_network_download_speed_mb_s",
        "hwinfo_network_upload_speed_mbps",
        "hwinfo_network_upload_speed_mb_s",
        "hwinfo_drive_used_percent",
        "hwinfo_drive_free_space_gb"
    ];

    private readonly HWiNFOSharedMemoryService _service;
    private readonly ILogger _logger;
    private readonly VariableDefinition[] _variables;

    public HWiNFOVariableProvider(HWiNFOSharedMemoryService service, ILogger logger)
    {
        _service = service;
        _logger = logger.ForContext<HWiNFOVariableProvider>();
        _logger.Information("[Variables] Registering HWiNFO variables...");
        _variables = RegisterVariables();
        foreach (var definition in _variables)
            _logger.Information("[Variables] Registered: {VariableName}", definition.Name);
        _logger.Information("[Variables] HWiNFO variables registered: {Count}", _variables.Length);
        }

        public IReadOnlyList<VariableDefinition> Variables => _variables;
        public IReadOnlyList<VariableDefinition> DeclaredVariables => _variables;
    public bool VariablesDependOnConfiguration => false;
    public bool SupportsCatalog => false;
    public bool SupportsPush => false;
    public bool SupportsSearch => false;
    public string CatalogName => "HWiNFO";
    public int? CatalogEntryCount => _variables.Length;

    private VariableDefinition[] RegisterVariables()
    {
        try
        {
            var definitions = TextVariableIds
                .Select(id => CreateDefinition(id, VariableType.Text))
                .Concat(NumericVariableIds.Select(id => CreateDefinition(id, VariableType.Numeric)))
                .ToArray();

            return definitions;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "[Variables] HWiNFO variable registration failed");
            throw;
        }
    }

    private static VariableDefinition CreateDefinition(string id, VariableType type)
    {
        var localId = id.Replace('_', '-');
        var definition = type == VariableType.Numeric
            ? VariableDefinition.Eager(id, type, 2)
            : VariableDefinition.Eager(id, type);

        return definition with
        {
            Id = localId,
            Name = id,
            DisplayName = id,
            Description = "HWiNFO Shared Memory value"
        };
    }

    public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken)
    {
        var variableName = localId.Replace('-', '_');
        if (variableName.Equals("hwinfo_status", StringComparison.OrdinalIgnoreCase))
            return ValueTask.FromResult(VariableReading.Of(_service.Status));
        if (variableName.Equals("hwinfo_version", StringComparison.OrdinalIgnoreCase))
            return ValueTask.FromResult(
                string.IsNullOrWhiteSpace(_service.Version)
                    ? VariableReading.Unavailable
                    : VariableReading.Of(_service.Version));
        if (variableName.Equals("hwinfo_sensor_count", StringComparison.OrdinalIgnoreCase))
            return ValueTask.FromResult(VariableReading.Of(_service.Readings.Count));
        if (variableName.Equals("macrodeck_version", StringComparison.OrdinalIgnoreCase))
            return ValueTask.FromResult(
                MacroDeckHostInfo.MacroDeckVersion is { } hostVersion
                    ? VariableReading.Of(hostVersion)
                    : VariableReading.Unavailable);
        if (variableName.Equals("macrodeck_sdk_version", StringComparison.OrdinalIgnoreCase))
            return ValueTask.FromResult(VariableReading.Of(MacroDeckHostInfo.SdkVersion));

        if (variableName.Equals("hwinfo_cpu_name", StringComparison.OrdinalIgnoreCase))
        {
            var cpuReading = FindReading("CPU");
            return ValueTask.FromResult(
                cpuReading is null
                    ? VariableReading.Unavailable
                    : VariableReading.Of(cpuReading.SensorName));
        }

        var reading = variableName switch
        {
            "hwinfo_cpu_usage" => FindReading("CPU Usage", "Total CPU Usage"),
            "hwinfo_cpu_temperature" => FindCpuTemperature(),
            "hwinfo_cpu_package_temperature" => FindCpuPackageTemperature(),
            "hwinfo_cpu_clock" => FindReading("CPU Clock", "Core Clock"),
            "hwinfo_cpu_effective_clock" => FindReading("Effective Clock"),
            "hwinfo_cpu_power" => FindReading("CPU Package Power", "CPU Power"),
            "hwinfo_cpu_voltage" => FindReading("CPU Core Voltage", "Vcore"),
            "hwinfo_cpu_fan_rpm" => FindReading("CPU Fan", "CPU"),
            "hwinfo_drive_used_percent" => FindDriveReading(
                "used percent",
                SensorAliases.FindDriveUsedPercent,
                fixedDrive => fixedDrive.UsedPercent),
            "hwinfo_drive_free_space_gb" => FindDriveReading(
                "free space",
                SensorAliases.FindDriveFreeGigabytes,
                fixedDrive => fixedDrive.FreeGigabytes),
            _ => null
        };

        if (reading is not null)
            return ValueTask.FromResult(VariableReading.Of(reading.Value));

        if (variableName.StartsWith("hwinfo_network_", StringComparison.OrdinalIgnoreCase))
        {
            var download = variableName.Contains("download", StringComparison.OrdinalIgnoreCase);
            var network = download
                ? SensorAliases.FindDownloadReading(_service.Readings)
                : SensorAliases.FindUploadReading(_service.Readings);
            _logger.Debug(
                "[Variables] Network {Direction} lookup: {Result}",
                download ? "download" : "upload",
                network is null ? "unavailable" : $"{network.SensorName} / {network.ReadingName} = {network.Value} {network.Unit}");
            var value = variableName.EndsWith("mbps", StringComparison.OrdinalIgnoreCase)
                ? NetworkSpeedConverter.ToMegabitsPerSecond(network)
                : NetworkSpeedConverter.ToMegabytesPerSecond(network);
            return ValueTask.FromResult(
                value is null
                    ? VariableReading.Unavailable
                    : VariableReading.Of(value.Value));
        }

        return ValueTask.FromResult(VariableReading.Unavailable);
    }

    private HWiNFOSensor? FindDriveReading(
        string what,
        Func<IReadOnlyList<HWiNFOSensor>, double?> fromHwinfo,
        Func<(double UsedPercent, double FreeGigabytes, string Name), double> fromFixedDrive)
    {
        var value = fromHwinfo(_service.Readings);
        if (value is not null)
            return new HWiNFOSensor(0, "HWiNFO", 0, what, "", value.Value);

        var fixedDrive = SensorAliases.ReadFixedDrive();
        _logger.Debug(
            "[Variables] Drive {What} lookup: {Result}",
            what,
            fixedDrive is null ? "unavailable" : $"fixed drive {fixedDrive.Value.Name}");
        return fixedDrive is null
            ? null
            : new HWiNFOSensor(0, fixedDrive.Value.Name, 0, what, "", fromFixedDrive(fixedDrive.Value));
    }

    private HWiNFOSensor? FindCpuTemperature()
    {
        return FindCpuTemperatureReading("CPU (Tctl/Tdie)");
    }

    private HWiNFOSensor? FindCpuPackageTemperature()
    {
        return FindCpuTemperatureReading("CPU (Tctl/Tdie)", "CPU Die (average)");
    }

    private HWiNFOSensor? FindCpuTemperatureReading(params string[] readingNames)
    {
        var reading = _service.Readings.FirstOrDefault(x =>
            x.SensorName.Contains("CPU", StringComparison.OrdinalIgnoreCase) &&
            readingNames.Any(name => x.ReadingName.Equals(name, StringComparison.OrdinalIgnoreCase)) &&
            x.Unit.Contains('C', StringComparison.OrdinalIgnoreCase));

        _logger.Debug(
            "[Variables] CPU temperature lookup ({ReadingNames}): {Result}",
            string.Join(", ", readingNames),
            reading is null ? "unavailable" : $"{reading.Value} {reading.Unit}");
        return reading;
    }

    private HWiNFOSensor? FindReading(params string[] terms)
    {
        foreach (var term in terms)
        {
            var reading = _service.Find(term);
            if (reading is not null)
                return reading;
        }

        return null;
    }

    public ValueTask<VariableDefinition?> ResolveAsync(
        string localId,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<VariableDefinition?>(
            _variables.FirstOrDefault(x => string.Equals(x.Id, localId, StringComparison.OrdinalIgnoreCase)));

    public ValueTask<VariableCatalogPage> DiscoverAsync(
        VariableCatalogQuery query,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new VariableCatalogPage
        {
            Items = _variables,
            ContinuationToken = null
        });

    public async ValueTask<IReadOnlyList<VariableValue>> SubscribeAsync(
        IReadOnlyCollection<string> localIds,
        CancellationToken cancellationToken)
    {
        var values = await Task.WhenAll(
            localIds.Select(async id => VariableValue.Of(id, await ReadAsync(id, cancellationToken))));
        return values;
    }

    public Task OnAttachedAsync(IVariableSink sink, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public ValueTask<VariableWriteResult> SetValueAsync(
        string localId,
        object? value,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(VariableWriteResult.NotWritable("HWiNFO sensors are read-only."));
}
