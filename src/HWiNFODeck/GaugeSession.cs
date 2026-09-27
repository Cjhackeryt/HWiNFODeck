using MacroDeck.Sdk.Variables;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Model.Surfaces;
using Serilog;

namespace HWiNFODeck;

internal sealed class GaugeSession : LiveReadingSession
{
    private const double SampleReading = 42;

    public GaugeSession(
        UiSurface surface,
        GaugeData data,
        UiWidgetAppearanceValues appearance,
        HWiNFOVariableProvider ownVariables,
        IVariableApi? hostVariables,
        WidgetRefreshOptions options,
        ILogger logger,
        bool live)
        : base(
            surface,
            data,
            ownVariables,
            hostVariables,
            options,
            logger,
            live,
            (ratio, display, hasValue) => GaugeWidget.BuildTree(ratio, display, hasValue, data, appearance))
    {
    }

    protected override double SampleValue => SampleReading;

    protected override string LogTag => "Gauge";
}
