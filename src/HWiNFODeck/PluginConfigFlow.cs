using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;

namespace HWiNFODeck;

internal sealed class PluginConfigFlow : IConfigFlow
{
    public const string StepId = "refresh";
    public const string RefreshKey = "refreshSeconds";

    public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ConfigFlowResult.Step(RefreshStep()));

    public Task<ConfigFlowResult> SubmitAsync(
        string stepId,
        IReadOnlyDictionary<string, object?> input,
        IConfigFlowContext context,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(stepId, StepId, StringComparison.Ordinal))
            return Task.FromResult(ConfigFlowResult.Step(RefreshStep()));

        if (!input.TryGetValue(RefreshKey, out var raw) || !TryParseSeconds(raw, out var seconds))
            return Task.FromResult(ConfigFlowResult.Error(
                RefreshStep(),
                Strings.Config.Refresh.Field.Invalid(),
                new Dictionary<string, LocalizedText>
                {
                    [RefreshKey] = Strings.Config.Refresh.Field.Invalid()
                }));

        return Task.FromResult(ConfigFlowResult.Complete(
            "HWiNFODeck",
            new Dictionary<string, ConfigFlowValue>
            {
                [RefreshKey] = ConfigFlowValue.Plain(seconds.ToString(CultureInfo.InvariantCulture))
            }));
    }

    private static ConfigFlowStep RefreshStep() => new()
    {
        StepId = StepId,
        Title = Strings.Config.Refresh.Title(),
        Description = Strings.Config.Refresh.Description(),
        Fields =
        [
            ActionParameter.Number(
                RefreshKey,
                Strings.Config.Refresh.Field.Label(),
                Strings.Config.Refresh.Field.Description(),
                GaugeScale.MinRefreshSeconds,
                GaugeScale.MaxRefreshSeconds,
                0.25,
                GaugeScale.DefaultRefreshSeconds,
                required: true)
        ]
    };

    private static bool TryParseSeconds(object? raw, out double seconds)
    {
        seconds = raw switch
        {
            double number => number,
            float number => number,
            int number => number,
            long number => number,
            decimal number => (double)number,
            string text when double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => double.NaN
        };
        if (!double.IsFinite(seconds) ||
            seconds < GaugeScale.MinRefreshSeconds ||
            seconds > GaugeScale.MaxRefreshSeconds)
        {
            seconds = 0;
            return false;
        }

        return true;
    }
}