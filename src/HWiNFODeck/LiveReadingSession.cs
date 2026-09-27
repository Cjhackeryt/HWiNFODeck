using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using Serilog;

namespace HWiNFODeck;

/// <summary>
/// Builds the tree a gauge widget renders, from the three states a live reading writes.
/// </summary>
internal delegate UiElement GaugeRootFactory(
    UiState<double> ratio,
    UiState<double> display,
    UiState<bool> hasValue);

/// <summary>
/// The live half of a gauge-style widget: it opens the view, polls the configured variable and
/// patches the three states the tree renders from. A subclass owns the rendering, its sample
/// reading and its log tag. The root is a factory rather than an override because the view has to
/// exist before this constructor returns, which is before a subclass's own fields are assigned.
/// </summary>
internal abstract class LiveReadingSession : IUiSession, IDisposable, IAsyncDisposable
{
    private readonly UiView _view;
    private readonly UiState<double> _ratio = new(0);
    private readonly UiState<double> _display = new(0);
    private readonly UiState<bool> _hasValue = new(false);
    private readonly string _variable;
    private readonly double _max;
    private readonly HWiNFOVariableProvider _ownVariables;
    private readonly IVariableApi? _hostVariables;
    private readonly WidgetRefreshOptions _options;
    private readonly ILogger _logger;
    private readonly Timer? _timer;
    private readonly CancellationTokenSource _refresh = new();
    private bool _disposed;

    protected LiveReadingSession(
        UiSurface surface,
        GaugeData data,
        HWiNFOVariableProvider ownVariables,
        IVariableApi? hostVariables,
        WidgetRefreshOptions options,
        ILogger logger,
        bool live,
        GaugeRootFactory createRoot)
    {
        _variable = data.Variable;
        _max = data.Max;
        _ownVariables = ownVariables;
        _hostVariables = hostVariables;
        _options = options;
        _logger = logger.ForContext(GetType());
        if (!live)
        {
            _display.Set(SampleValue);
            _ratio.Set(GaugeScale.ToRatio(SampleValue, _max));
            _hasValue.Set(true);
        }

        _view = new UiView(surface, createRoot(_ratio, _display, _hasValue));
        _view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _view.HandlerFaulted += (_, fault) =>
            Faulted?.Invoke(this, new UiSessionFaultedEventArgs("ui-handler-fault", fault.Exception));
        if (live && !string.IsNullOrWhiteSpace(_variable))
            _timer = new Timer(OnTick, null, TimeSpan.Zero, RefreshPeriod(options.RefreshSeconds));
    }

    /// <summary>The reading a picker card shows, so the tile is never blank before it is configured.</summary>
    protected abstract double SampleValue { get; }

    /// <summary>Distinguishes the widgets in the host log, which stamps only the plugin identity.</summary>
    protected abstract string LogTag { get; }

    public event EventHandler? Changed;
    public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

    public UiTree BuildTree() => _view.Tree;

    public IReadOnlyList<UiPatch> DrainPatches() => _view.DrainPatches();

    public void Dispatch(UiEvent e) => _view.Dispatch(e);

    internal Task RefreshNowAsync() => RefreshAsync();

    internal static TimeSpan RefreshPeriod(double refreshSeconds) =>
        TimeSpan.FromSeconds(GaugeScale.CoerceRefreshSeconds(refreshSeconds));

    private void OnTick(object? _) => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        try
        {
            if (_disposed || _refresh.IsCancellationRequested)
                return;

            _timer?.Change(RefreshPeriod(_options.RefreshSeconds), RefreshPeriod(_options.RefreshSeconds));
            var value = await ReadOwnAsync(_refresh.Token) ?? await ReadHostAsync();
            if (value is null)
                return;

            if (_hasValue.Peek() && value.Value.Equals(_display.Peek()))
                return;

            _display.Set(value.Value);
            _ratio.Set(GaugeScale.ToRatio(value.Value, _max));
            if (!_hasValue.Peek())
                _hasValue.Set(true);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            _logger.Debug(exception, $"[{LogTag}] Variable refresh failed");
        }
    }

    private async ValueTask<double?> ReadOwnAsync(CancellationToken cancellationToken)
    {
        var localId = GaugeScale.OwnLocalId(_ownVariables.Variables, _variable);
        if (localId is null)
            return null;

        var reading = await _ownVariables.ReadAsync(localId, cancellationToken);
        return GaugeScale.ToNumber(reading.Value);
    }

    private async Task<double?> ReadHostAsync()
    {
        if (_hostVariables is null)
            return null;

        try
        {
            var handle = await _hostVariables.GetByNameAsync(_variable);
            var direct = handle is null ? null : GaugeScale.ToNumber(handle.Value);
            if (direct is not null)
                return direct;
        }
        catch (Exception exception)
        {
            _logger.Debug(exception, $"[{LogTag}] Host variable lookup failed");
        }

        var handles = await _hostVariables.GetAllAsync();
        return GaugeScale.ResolveValue(handles, _variable);
    }

    public void Dispose()
    {
        DisposeCore();
        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        DisposeCore();
        return ValueTask.CompletedTask;
    }

    private void DisposeCore()
    {
        if (_disposed)
            return;
        _disposed = true;
        _timer?.Dispose();
        _refresh.Cancel();
        _refresh.Dispose();
    }
}
