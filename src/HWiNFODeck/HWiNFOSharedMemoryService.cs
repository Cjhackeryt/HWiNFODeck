using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Text;
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
    public event Action? Updated;

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
                _logger.Error(exception, "[HWiNFO] Shared Memory read failed");
                ClearReadings();
                SetStatus("Connection error");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }
    }

    private void ReadSharedMemory()
    {
        var process = FindHWiNFOProcess();
        _logger.Information("[HWiNFO] Process detected: {Detected}", process is not null ? "YES" : "NO");
        _logger.Information("[HWiNFO] Process name: {ProcessName}", process?.ProcessName ?? "none");
        _logger.Information("[HWiNFO] Searching for Shared Memory...");

        if (process is null)
        {
            CloseMap();
            ClearReadings();
            SetStatus("HWiNFO not running");
            _logger.Information("[HWiNFO] Shared Memory mapping found: NO");
            _logger.Information("[HWiNFO] Shared Memory opened: NO");
            return;
        }

        if (_map is null)
        {
            if (!OperatingSystem.IsWindows())
            {
                _logger.Information("[HWiNFO] Shared Memory is Windows-only");
                ClearReadings();
                SetStatus("Windows required");
                return;
            }

            try
            {
                _map = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read);
                _logger.Information("[HWiNFO] Shared Memory mapping found: YES");
                _logger.Information("[HWiNFO] Shared Memory opened: YES");
            }
            catch (FileNotFoundException)
            {
                _logger.Information("[HWiNFO] Shared Memory mapping found: NO");
                _logger.Information("[HWiNFO] Shared Memory opened: NO");
                ClearReadings();
                SetStatus("HWiNFO running - Shared Memory unavailable");
                return;
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "[HWiNFO] Shared Memory opened: NO");
                ClearReadings();
                SetStatus("Connection error");
                return;
            }
        }
        else
        {
            _logger.Information("[HWiNFO] Shared Memory mapping found: YES");
            _logger.Information("[HWiNFO] Shared Memory opened: YES");
        }

        try
        {
            using var accessor = _map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            var length = accessor.Capacity;
            if (length < HeaderSize)
            {
                _logger.Warning("[HWiNFO] Header valid: NO (mapping is {Length} bytes)", length);
                ClearReadings();
                SetStatus("HWiNFO running - Shared Memory unavailable");
                return;
            }

            var header = ReadHeader(accessor);
            var headerValid = ValidateHeader(header, length);
            _logger.Information("[HWiNFO] Header valid: {Valid}", headerValid ? "YES" : "NO");
            _logger.Information(
                "[HWiNFO] Version: {Version}; Revision: {Revision}; Mapping size: {Length}; Sensor offset/size/count: {SensorOffset}/{SensorSize}/{SensorCount}; Reading offset/size/count: {ReadingOffset}/{ReadingSize}/{ReadingCount}",
                header.Version,
                header.Revision,
                length,
                header.SensorOffset,
                header.SensorSize,
                header.SensorCount,
                header.ReadingOffset,
                header.ReadingSize,
                header.ReadingCount);
            Version = $"SM2 layout {header.Version}";

            if (!headerValid)
            {
                ClearReadings();
                SetStatus("HWiNFO running - Shared Memory unavailable");
                return;
            }

            var discovered = ParseEntries(accessor, length, header).ToArray();
            lock (_gate)
            {
                _readings.Clear();
                foreach (var reading in discovered)
                    if (double.IsFinite(reading.Value))
                        _readings[reading.VariableId] = reading;
            }

            _logger.Information("[HWiNFO] Sensor count: {Count}", discovered.Length);
            foreach (var reading in discovered)
            {
                _logger.Information(
                    "[HWiNFO] Sensor: {Sensor}; Reading: {Reading}; Unit: {Unit}; Value: {Value}",
                    reading.SensorName,
                    reading.ReadingName,
                    reading.Unit,
                    reading.Value);
            }

            SetStatus("Connected");
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "[HWiNFO] Shared Memory read failed");
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
        return header.Signature == Signature
            && header.Version is >= 1 and <= 2
            && header.SensorSize >= 264
            && header.ReadingSize >= 316
            && ValidTable(header.SensorOffset, header.SensorSize, header.SensorCount, length)
            && ValidTable(header.ReadingOffset, header.ReadingSize, header.ReadingCount, length);
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
        if (!valid)
        {
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
        Updated?.Invoke();
    }

    private void ClearReadings()
    {
        lock (_gate)
            _readings.Clear();
        Updated?.Invoke();
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
