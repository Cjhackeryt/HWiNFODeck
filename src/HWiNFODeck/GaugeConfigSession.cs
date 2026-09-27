using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

namespace HWiNFODeck;

internal sealed class GaugeConfigSession : IUiSession
{
    private readonly UiView _view;

    public GaugeConfigSession(UiSurface surface, GaugeData data, JsonElement storedData)
    {
        var variable = new UiState<string>(data.Variable);
        var max = new UiState<double>(data.Max);
        var unit = new UiState<string>(data.Unit);
        _view = new UiView(surface, new UiWidgetConfiguration
        {
            Key = "root",
            Properties = new UiWidgetProperties
            {
                Key = "properties",
                Children =
                [
                    new UiVariablePickerInput
                    {
                        Key = "variable",
                        Label = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.Variable.Label()),
                        Binding = Bind.To(variable)
                    },
                    new UiNumberInput
                    {
                        Key = "max",
                        Label = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.Max.Label()),
                        Min = UiValue.Of(0d),
                        Step = UiValue.Of(1d),
                        Binding = Bind.To(max)
                    },
                    new UiStringInput
                    {
                        Key = "unit",
                        Label = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.Unit.Label()),
                        Binding = Bind.To(unit)
                    },
                    UiWidgetAppearance.Section(storedData, UiWidgetAppearanceFields.BackgroundColor | UiWidgetAppearanceFields.AccentColor)
                ]
            }
        });
        _view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _view.HandlerFaulted += (_, fault) =>
            Faulted?.Invoke(this, new UiSessionFaultedEventArgs("ui-handler-fault", fault.Exception));
    }

    public event EventHandler? Changed;
    public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

    public UiTree BuildTree() => _view.Tree;

    public IReadOnlyList<UiPatch> DrainPatches() => _view.DrainPatches();

    public void Dispatch(UiEvent e) => _view.Dispatch(e);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
