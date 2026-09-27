using MacroDeck.Localization;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;

namespace HWiNFODeck;

/// <summary>
/// The arc gauge widget type: a variable drawn as a fraction of its maximum on Macro Deck's own
/// <c>ui.gauge</c>, with the reading in the middle. Drawn by the UI runtime rather than by
/// hand-placed transforms, so it needs no artwork upload and no per-reader geometry of its own.
/// </summary>
internal static class GaugeWidget
{
    public const string WidgetTypeId = "gauge";
    public static readonly string QualifiedWidgetTypeId = GaugeScale.QualifiedId(WidgetTypeId);
    public const double ArcStartAngle = -135;
    public const double ArcEndAngle = 135;
    public const string DefaultLevelColor = "#2B6CEE";
    private const double ArcThickness = 0.18;
    private const double ValueSize = 0.16;
    private const double UnitSize = 0.08;
    private const double MaxSize = 0.07;
    private const double LabelGap = 0.01;

    public const string DefaultData = """{"variable":"","max":100.0,"unit":""}""";

    /// <summary>
    /// The schema has to accept the appearance keys this type declares, otherwise the appearance
    /// actions refuse the widget instead of writing to its data.
    /// </summary>
    public const string DataSchema =
        """{"type":"object","properties":{"variable":{"type":"string"},"max":{"type":"number"},"unit":{"type":"string"},"border":{"type":"object"},"backgroundColor":{"type":"string"},"label":{"type":"string"},"labelColor":{"type":"string"},"fontFaceId":{"type":"string"},"fontSize":{"type":"number"},"textAlign":{"type":"string"},"labelPosition":{"type":"string"},"accentColor":{"type":"string"}},"required":["variable","max"]}""";

    public static WidgetTypeDescriptor Descriptor => new(
        WidgetTypeId,
        LocalizedText.FromLocalized(Strings.Widgets.Gauge.Name()),
        LocalizedText.FromLocalized(Strings.Widgets.Gauge.Description()),
        DefaultData,
        DataSchema,
        true,
        new Dictionary<string, string>())
    {
        AppearanceProperties =
        [
            WidgetAppearanceProperty.BackgroundColor,
            WidgetAppearanceProperty.AccentColor
        ]
    };

    public static UiElement BuildTree(
        UiState<double> ratio,
        UiState<double> display,
        UiState<bool> hasValue,
        GaugeData data,
        UiWidgetAppearanceValues appearance)
    {
        var levelColor = appearance.AccentColor ?? DefaultLevelColor;
        var layer = new UiLayer
        {
            Key = "gauge",
            Children =
            [
                new UiGauge
                {
                    Key = "arc",
                    Level = UiValue.From(() => ratio.Value),
                    StartAngle = UiValue.Of(ArcStartAngle),
                    EndAngle = UiValue.Of(ArcEndAngle),
                    Thickness = UiSize.FromBasis(ArcThickness),
                    LevelColor = UiValue.Of(levelColor),
                    Fallback = new UiRangeBar
                    {
                        Key = "arc-fallback",
                        Start = UiValue.Of(0d),
                        End = UiValue.From(() => ratio.Value),
                        StartColor = UiValue.Of(levelColor),
                        EndColor = UiValue.Of(levelColor)
                    }
                },
                BuildLabels(display, hasValue, data)
            ]
        };

        // A provider's widget draws its own background; Macro Deck only draws the border.
        return appearance.BackgroundColor is null
            ? layer
            : new UiModifier
            {
                Key = "background",
                Background = UiBackgroundValue.From(() => UiBackground.Solid(appearance.BackgroundColor)),
                Child = layer
            };
    }

    /// <summary>
    /// The value, its unit and the maximum, stacked on three centred lines. A layer hands every
    /// child the whole box and offers no alignment of its own, so the centring is this stack's job.
    /// </summary>
    private static UiStack BuildLabels(UiState<double> display, UiState<bool> hasValue, GaugeData data)
    {
        var children = new List<UiElement>
        {
            new UiTextRun
            {
                Key = "value",
                Text = UiText.From(() => GaugeScale.FormatValue(display.Value, hasValue.Value)),
                Size = UiSize.FromBasis(ValueSize),
                Weight = UiValue.Of(UiComponentTextWeights.Bold),
                Role = UiValue.Of(UiComponentTextRoles.Primary),
                Align = UiValue.Of(UiComponentAlignments.Center)
            }
        };

        // An unset unit leaves the line out rather than reserving an empty one.
        if (!string.IsNullOrWhiteSpace(data.Unit))
            children.Add(new UiTextRun
            {
                Key = "unit",
                Text = UiText.Of(data.Unit),
                Size = UiSize.FromBasis(UnitSize),
                Role = UiValue.Of(UiComponentTextRoles.Muted),
                Align = UiValue.Of(UiComponentAlignments.Center)
            });

        children.Add(new UiTextRun
        {
            Key = "max",
            Text = UiText.Of(GaugeScale.FormatMax(data.Max)),
            Size = UiSize.FromBasis(MaxSize),
            Role = UiValue.Of(UiComponentTextRoles.Muted),
            Align = UiValue.Of(UiComponentAlignments.Center)
        });

        return new UiStack
        {
            Key = "labels",
            Justify = UiValue.Of(UiComponentJustify.Center),
            Align = UiValue.Of(UiComponentAlignments.Center),
            Gap = UiSize.FromBasis(LabelGap),
            Children = children
        };
    }
}
