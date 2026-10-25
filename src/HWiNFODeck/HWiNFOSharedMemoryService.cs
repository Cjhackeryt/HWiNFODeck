using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Text;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace HWiNFODeck;

public sealed class HWiNFOSharedMemoryService : IAsyncDisposable
{
    private const string MapName = "Global\\HWiNFO_SENS_SM2";
    private const uint Signature = 0x53695748; // "HWiS" in little-endian memory order
    private const int HeaderSize = 48;
    private const int SensorNameOffset = 8;
    private const int ReadingTypeOffset = 0;
    private const int ReadingSensorIndexOffset = 4;
    private const int ReadingIdOffset = 8;
    private const int ReadingNameOffset = 12;
    private const int ReadingUnitOffset = 268;
    private const int ReadingValueOffset = 284;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly Dictionary<string, HWiNFOSensor> _readings = new(StringComparer.OrdinalIgnoreCase);
    private MemoryMappedFile? _map;
    private CancellationTokenSource? _polling;
    private Task? _pollTask;
    private readonly FailureEpisodeTracker _readFailures = new();
    private string? _lastInvalidTable;
    private bool _hasLoggedMapOpen;

    public HWiNFOSharedMemoryService(ILogger logger) => _logger = logger.ForContext<HWiNFOSharedMemoryService>();
    public string Status { get; private set; } = "HWiNFO not running";
    public string Version { get; private set; } = "";
    public IReadOnlyList<HWiNFOSensor> Readings { get { lock (_gate) return _readings.Values.ToArray(); } }
    public HWiNFOSensor? Find(string? terms)
    {
        if (string.IsNullOrWhiteSpace(terms)) return null;
        var words = terms.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        lock (_gate)
            return _readings.Values.FirstOrDefault(x =>
                words.All(word => $"{x.SensorName} {x.ReadingName}".Contains(word, StringComparison.OrdinalIgnoreCase)));
    }
    public int ReadingCount { get { lock (_gate) return _readings.Count; } }
    public event Action? Updated;
    public event Action? CatalogChanged;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.Information("[HWiNFO] Initializing");
        _polling = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pollTask = PollAsync(_polling.Token);
        return Task.CompletedTask;
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try { ReadSharedMemory(); }
            catch (Exception exception)
            {
                LogReadFailure(exception);
                ClearReadings();
                SetStatus("Connection error");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }
    }

    private void ReadSharedMemory()
    {
        using var process = FindHWiNFOProcess();

        if (process is null)
        {
            CloseMap();
            ClearReadings();
            SetStatus("HWiNFO not running");
            _hasLoggedMapOpen = false;
            RecordReadSuccess();
            return;
        }

        if (_map is null)
        {
            if (!OperatingSystem.IsWindows())
            {
                ClearReadings();
                SetStatus("Windows required");
                RecordReadSuccess();
                return;
            }

            try
            {
                _map = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read);
                if (!_hasLoggedMapOpen)
                {
                    _logger.Information("[HWiNFO] Shared Memory mapping opened");
                    _hasLoggedMapOpen = true;
                }
            }
            catch (FileNotFoundException)
            {
                ClearReadings();
                SetStatus("HWiNFO running - Shared Memory unavailable");
                RecordReadSuccess();
                return;
            }
            catch (Exception exception)
            {
                LogReadFailure(exception);
                ClearReadings();
                SetStatus("Connection error");
                return;
            }
        }

        try
        {
            using var accessor = _map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            var length = accessor.Capacity;
            if (length < HeaderSize)
            {
                ClearReadings();
                SetStatus("HWiNFO running - Shared Memory unavailable");
                RecordReadSuccess();
                return;
            }

            var header = ReadHeader(accessor);
            var headerValid = ValidateHeader(header, length);
            Version = $"SM2 layout {header.Version}";

            if (!headerValid)
            {
                ClearReadings();
                SetStatus("HWiNFO running - Shared Memory unavailable");
                RecordReadSuccess();
                return;
            }

            var discovered = ParseEntries(accessor, length, header)
                .Where(reading => double.IsFinite(reading.Value))
                .ToArray();
            if (ReplaceReadings(discovered))
            {
                var gpuCount = discovered
                    .Where(reading => SensorAliases.IsGpuSensor(reading.SensorName))
                    .Select(reading => reading.SensorId)
                    .Distinct()
                    .Count();
                _logger.Information(
                    "[HWiNFO] Sensor catalog updated: {ReadingCount} readings across {GpuCount} GPU sensors",
                    discovered.Length,
                    gpuCount);
            }

            RecordReadSuccess(connected: true);
            SetStatus("Connected");
        }
        catch (Exception exception)
        {
            LogReadFailure(exception);
            CloseMap();
            ClearReadings();
            SetStatus("Connection error");
        }
    }

    private static Process? FindHWiNFOProcess()
    {
        return Process.GetProcessesByName("HWiNFO64").FirstOrDefault()
            ?? Process.GetProcessesByName("HWiNFO32").FirstOrDefault();
    }

    private static SharedMemoryHeader ReadHeader(MemoryMappedViewAccessor accessor) =>
        new(
            accessor.ReadUInt32(0),
            accessor.ReadUInt32(4),
            accessor.ReadUInt32(8),
            accessor.ReadUInt32(20),
            accessor.ReadUInt32(24),
            accessor.ReadUInt32(28),
            accessor.ReadUInt32(32),
            accessor.ReadUInt32(36),
            accessor.ReadUInt32(40));

    private bool ValidateHeader(SharedMemoryHeader header, long length)
    {
        var valid = header.Signature == Signature
            && header.Version is >= 1 and <= 2
            && header.SensorSize >= 264
            && header.ReadingSize >= 316
            && ValidTable(header.SensorOffset, header.SensorSize, header.SensorCount, length)
            && ValidTable(header.ReadingOffset, header.ReadingSize, header.ReadingCount, length);
        if (valid)
            _lastInvalidTable = null;

        return valid;
    }

    private static IEnumerable<HWiNFOSensor> ParseEntries(
        MemoryMappedViewAccessor accessor,
        long length,
        SharedMemoryHeader header)
    {
        var sensors = new Dictionary<uint, string>();
        for (uint i = 0; i < header.SensorCount; i++)
        {
            var position = header.SensorOffset + (ulong)i * header.SensorSize;
            var id = accessor.ReadUInt32((long)position);
            var name = ReadString(accessor, (long)position + SensorNameOffset, 128);
            if (id != 0 && name.Length > 0) sensors[id] = name;
        }
        for (uint i = 0; i < header.ReadingCount; i++)
        {
            var position = header.ReadingOffset + (ulong)i * header.ReadingSize;
            var sensorIndex = accessor.ReadUInt32((long)position + ReadingSensorIndexOffset);
            var readingId = accessor.ReadUInt32((long)position + ReadingIdOffset);
            var valueOffset = (long)position + ReadingValueOffset;
            if (valueOffset + 8 > length || sensorIndex >= header.SensorCount || readingId == 0)
                continue;

            var sensorPosition = header.SensorOffset + (ulong)sensorIndex * header.SensorSize;
            var sensorId = accessor.ReadUInt32((long)sensorPosition);
            var sensorName = ReadString(accessor, (long)sensorPosition + SensorNameOffset, 128);
            var readingName = ReadString(accessor, (long)position + ReadingNameOffset, 128);
            var unit = ReadString(accessor, (long)position + ReadingUnitOffset, 16);
            var value = accessor.ReadDouble(valueOffset);
            if (sensorId != 0 && sensorName.Length > 0 && readingName.Length > 0)
                yield return new HWiNFOSensor(sensorId, sensorName, readingId, readingName, unit, value);
        }
    }

    private bool ValidTable(uint offset, uint size, uint count, long length)
    {
        var valid = size >= 8 && count < 100000 &&
            (ulong)offset + (ulong)size * count <= (ulong)length;
        if (!valid && _lastInvalidTable is null)
        {
            _lastInvalidTable = $"{offset}:{size}:{count}:{length}";
            _logger.Warning(
                "[HWiNFO] Invalid table: offset={Offset}, size={Size}, count={Count}, end={End}, mapping={Length}",
                offset,
                size,
                count,
                (ulong)offset + (ulong)size * count,
                length);
        }

        return valid;
    }

    private static string ReadString(MemoryMappedViewAccessor accessor, long offset, int maxBytes)
    {
        if (maxBytes <= 0) return "";
        var bytes = new byte[maxBytes];
        accessor.ReadArray(offset, bytes, 0, bytes.Length);
        var zero = Array.IndexOf(bytes, (byte)0);
        if (zero >= 0) Array.Resize(ref bytes, zero);
        return Encoding.ASCII.GetString(bytes).Trim();
    }

    private void SetStatus(string status)
    {
        if (Status == status) return;
        Status = status;
        _logger.Information("[HWiNFO] Status: {Status}", status);
        Updated?.Invoke();
    }

    private void ClearReadings()
    {
        bool changed;
        lock (_gate)
        {
            changed = _readings.Count > 0;
            _readings.Clear();
        }

        if (changed)
        {
            CatalogChanged?.Invoke();
            Updated?.Invoke();
        }
    }

    internal bool ReplaceReadings(HWiNFOSensor[] readings)
    {
        bool catalogChanged;
        lock (_gate)
        {
            catalogChanged = _readings.Count != readings.Length ||
                readings.Any(reading =>
                    !_readings.TryGetValue(reading.VariableId, out var current) ||
                    current.SensorName != reading.SensorName ||
                    current.ReadingName != reading.ReadingName ||
                    current.Unit != reading.Unit);

            _readings.Clear();
            foreach (var reading in readings)
                _readings[reading.VariableId] = reading;
        }

        if (catalogChanged)
            CatalogChanged?.Invoke();

        return catalogChanged;
    }

    internal HWiNFOSensor? FindByVariableId(string variableId)
    {
        lock (_gate)
            return _readings.TryGetValue(variableId, out var reading) ? reading : null;
    }

    private void LogReadFailure(Exception exception)
    {
        var signal = _readFailures.RecordFailure(exception.Message);
        if (signal.Kind == FailureEpisodeSignalKind.Onset)
            _logger.Error(exception, "[HWiNFO] Shared Memory read failed");
        else if (signal.Kind == FailureEpisodeSignalKind.SummaryDue)
            _logger.Warning(
                "[HWiNFO] Shared Memory read still failing after {FailureCount} attempts: {LastError}",
                signal.ConsecutiveFailures,
                signal.LastError);
    }

    private void RecordReadSuccess(bool connected = false)
    {
        var episode = _readFailures.RecordSuccess();
        if (connected && episode is not null)
        {
            _logger.Information(
                "[HWiNFO] Shared Memory reads recovered after {FailureCount} failures over {Duration}",
                episode.Failures,
                episode.Duration);
        }
    }

    private void CloseMap()
    {
        _map?.Dispose();
        _map = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_polling is null) { CloseMap(); return; }
        _polling.Cancel();
        if (_pollTask is not null) await _pollTask;
        _polling.Dispose();
        _polling = null;
        CloseMap();
    }

    private readonly record struct SharedMemoryHeader(
        uint Signature,
        uint Version,
        uint Revision,
        uint SensorOffset,
        uint SensorSize,
        uint SensorCount,
        uint ReadingOffset,
        uint ReadingSize,
        uint ReadingCount);
}
