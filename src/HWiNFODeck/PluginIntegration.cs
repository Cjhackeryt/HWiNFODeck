using System.Globalization;
using System.Text.Json;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Model.Surfaces;
using Serilog;

namespace HWiNFODeck;

public sealed class PluginIntegration : IPluginIntegration, IVariableProvider, IWidgetTypeProvider, IUiProvider, IConfigFlowProvider, IAsyncDisposable
{
    private readonly HWiNFOSharedMemoryService _service;
    private readonly HWiNFOVariableProvider _variables;
    private readonly WidgetRefreshOptions _refreshOptions = new();
    private readonly ILogger _logger;
    private IVariableApi? _hostVariables;

    public PluginIntegration(ILogger logger)
    {
        _logger = logger.ForContext<PluginIntegration>();
        logger.Information("[HWiNFODeck] Plugin loaded");
        logger.Information("[HWiNFODeck] Version: {ManifestVersion}", ReadManifestVersion());
        logger.Information("[HWiNFODeck] Loaded from: {AssemblyPath}", typeof(PluginIntegration).Assembly.Location);
        _service = new HWiNFOSharedMemoryService(logger);
        _variables = new HWiNFOVariableProvider(_service, logger);
    }

    // The manifest is the version source of truth: the assembly version is only the SDK default, and
    // the release workflow rewrites the manifest from the release tag, so neither tracks a release.
    private string ReadManifestVersion()
    {
        try
        {
            var manifestPath = Path.Combine(AppContext.BaseDirectory, "manifest.json");
            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            return manifest.RootElement.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String
                ? version.GetString() ?? "unknown"
                : "unknown";
        }
        catch (Exception exception)
        {
            _logger.Debug(exception, "[HWiNFODeck] Manifest version could not be read");
            return "unknown";
        }
    }

    public IReadOnlyList<IActionDefinition> Actions => [];
    public IReadOnlyList<VariableDefinition> Variables => _variables.Variables;
    public IReadOnlyList<VariableDefinition> DeclaredVariables => _variables.DeclaredVariables;
    public bool VariablesDependOnConfiguration => _variables.VariablesDependOnConfiguration;
    public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken) =>
        _variables.ReadAsync(localId, cancellationToken);
    public ValueTask<VariableWriteResult> SetValueAsync(string localId, object? value, CancellationToken cancellationToken) =>
        _variables.SetValueAsync(localId, value, cancellationToken);
    public ValueTask<VariableDefinition?> ResolveAsync(string localId, CancellationToken cancellationToken) =>
        _variables.ResolveAsync(localId, cancellationToken);
    public ValueTask<VariableCatalogPage> DiscoverAsync(VariableCatalogQuery query, CancellationToken cancellationToken) =>
        _variables.DiscoverAsync(query, cancellationToken);
    public ValueTask<IReadOnlyList<VariableValue>> SubscribeAsync(
        IReadOnlyCollection<string> localIds, CancellationToken cancellationToken) =>
        _variables.SubscribeAsync(localIds, cancellationToken);
    public Task OnAttachedAsync(IVariableSink sink, CancellationToken cancellationToken) =>
        _variables.OnAttachedAsync(sink, cancellationToken);

    public string ProviderName => "HWiNFODeck";

    public IReadOnlyList<WidgetTypeDescriptor> GetWidgetTypes() => [GaugeWidget.Descriptor];

    public IReadOnlyList<UiSurfaceDeclaration> Surfaces =>
    [
        new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
        new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
        new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive }
    ];

    public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var surface = request.Surface;
        if (surface.Kind == UiSurfaceKinds.Config)
        {
            if (!IsWidgetConfig(surface, GaugeWidget.WidgetTypeId))
                return Task.FromResult<IUiSession?>(null);

            var stored = ReadRawData(surface, UiConfigSurfaceAttributes.WidgetData);
            return Task.FromResult<IUiSession?>(
                new GaugeConfigSession(surface, GaugeScale.ParseData(stored), stored));
        }

        if (surface.Kind != UiSurfaceKinds.Widget && surface.Kind != UiSurfaceKinds.Preview)
            return Task.FromResult<IUiSession?>(null);
        if (!IsWidget(surface, GaugeWidget.WidgetTypeId))
            return Task.FromResult<IUiSession?>(null);

        var isSample = surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Sample, out var sampleElement) &&
            sampleElement.ValueKind == JsonValueKind.True;
        var data = ReadRawData(surface, UiWidgetSurfaceAttributes.Data);
        return Task.FromResult<IUiSession?>(new GaugeSession(
            surface,
            GaugeScale.ParseData(data),
            UiWidgetAppearance.Read(data),
            _variables,
            _hostVariables,
            _refreshOptions,
            _logger,
            live: !isSample));
    }

    public bool AllowsMultipleConfigurations => false;

    public IConfigFlow CreateConfigFlow() => new PluginConfigFlow();

    public async Task InitializeAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken)
    {
        await context.RegisterWidgetTypeAsync(GaugeWidget.Descriptor, cancellationToken);
    }

    private static bool IsWidget(UiSurface surface, string widgetTypeId) =>
        TryGetType(surface, UiWidgetSurfaceAttributes.WidgetType, widgetTypeId);

    private static bool IsWidgetConfig(UiSurface surface, string widgetTypeId) =>
        TryGetType(surface, UiConfigSurfaceAttributes.EntryPoint, UiConfigEntryPoints.WidgetConfig) &&
        TryGetType(surface, UiConfigSurfaceAttributes.WidgetType, widgetTypeId);

    private static bool TryGetType(UiSurface surface, string attributeKey, string widgetTypeId)
    {
        if (!surface.Attributes.TryGetValue(attributeKey, out var element) || element.ValueKind != JsonValueKind.String)
            return false;
        var value = element.GetString();
        return value == widgetTypeId || value == GaugeScale.QualifiedId(widgetTypeId);
    }

    private static JsonElement ReadRawData(UiSurface surface, string key) =>
        surface.Attributes.TryGetValue(key, out var data) && data.ValueKind == JsonValueKind.Object
            ? data
            : default;

    public async Task InitializeAsync(IIntegrationContext context)
    {
        _hostVariables = context.Variables;
        _refreshOptions.RefreshSeconds = await ReadRefreshSecondsAsync(context);
        await _service.StartAsync(CancellationToken.None);
    }

    private async Task<double> ReadRefreshSecondsAsync(IIntegrationContext context)
    {
        try
        {
            var entries = await context.Config.GetEntriesAsync(CancellationToken.None);
            if (entries.Count == 0)
                return GaugeScale.DefaultRefreshSeconds;

            var raw = await context.Config.GetStringAsync(entries[0].Id, PluginConfigFlow.RefreshKey, CancellationToken.None);
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                ? GaugeScale.CoerceRefreshSeconds(seconds)
                : GaugeScale.DefaultRefreshSeconds;
        }
        catch (Exception exception)
        {
            _logger.Debug(exception, "[HWiNFODeck] Plugin configuration read failed, using defaults");
            return GaugeScale.DefaultRefreshSeconds;
        }
    }
    public async Task ShutdownAsync() => await _service.DisposeAsync();
    public ValueTask DisposeAsync() => _service.DisposeAsync();
}