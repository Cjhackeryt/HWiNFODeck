using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using NUnit.Framework;

namespace HWiNFODeck.Tests;

[TestFixture]
public sealed class GaugeWidgetTests
{
	private static readonly WidgetAppearanceProperty[] DeclaredAppearance =
		[WidgetAppearanceProperty.BackgroundColor, WidgetAppearanceProperty.AccentColor];

	private static readonly string[] ExpectedTreeKeys =
		["gauge", "arc", "labels", "value", "unit", "max"];

	private static readonly string[] ExpectedLabelLines =
		["gauge.labels.value", "gauge.labels.unit", "gauge.labels.max"];

	private static readonly string[] ExpectedLabelLinesWithoutUnit =
		["gauge.labels.value", "gauge.labels.max"];

	[Test]
	public void The_widget_type_id_is_qualified_by_the_plugin()
	{
		Assert.That(GaugeWidget.WidgetTypeId, Is.EqualTo("gauge"));
		Assert.That(GaugeWidget.QualifiedWidgetTypeId, Is.EqualTo("com.hwinfo.cjhackeryt::gauge"));
	}

	[Test]
	public void The_descriptor_configures_the_widget_and_offers_the_appearance_it_uses()
	{
		var descriptor = GaugeWidget.Descriptor;

		Assert.That(descriptor.Id, Is.EqualTo("gauge"));
		Assert.That(descriptor.HasConfiguration, Is.True);
		Assert.That(descriptor.DefaultData, Is.EqualTo(GaugeWidget.DefaultData));
		Assert.That(descriptor.DataSchema, Is.EqualTo(GaugeWidget.DataSchema));
		Assert.That(descriptor.AppearanceProperties, Is.EqualTo(DeclaredAppearance));
	}

	[TestCase("border")]
	[TestCase("backgroundColor")]
	[TestCase("accentColor")]
	public void The_schema_allows_every_appearance_key_the_descriptor_declares(string key)
	{
		using var schema = JsonDocument.Parse(GaugeWidget.DataSchema);

		Assert.That(schema.RootElement.GetProperty("properties").TryGetProperty(key, out _), Is.True);
	}

	[Test]
	public void The_default_data_parses_into_the_gauge_contract()
	{
		using var defaults = JsonDocument.Parse(GaugeWidget.DefaultData);

		Assert.That(GaugeScale.ParseData(defaults.RootElement), Is.EqualTo(new GaugeData("", 100, "")));
	}

	[Test]
	public void The_stored_data_parses()
	{
		using var stored = JsonDocument.Parse("""{"variable":"hwinfo_cpu_usage","max":200.0,"unit":"%","accentColor":"#ff0000"}""");

		Assert.That(GaugeScale.ParseData(stored.RootElement), Is.EqualTo(new GaugeData("hwinfo_cpu_usage", 200, "%")));
	}

	[Test]
	public void The_arc_sweeps_three_quarters_of_a_turn()
	{
		Assert.That(GaugeWidget.ArcStartAngle, Is.EqualTo(-135));
		Assert.That(GaugeWidget.ArcEndAngle, Is.EqualTo(135));
		Assert.That(GaugeWidget.ArcEndAngle - GaugeWidget.ArcStartAngle, Is.EqualTo(270));
	}

	[Test]
	public void The_gauge_tree_has_a_unique_key_per_node()
	{
		var root = GaugeWidget.BuildTree(
			new UiState<double>(0.42),
			new UiState<double>(42),
			new UiState<bool>(true),
			new GaugeData("test-var", 100, "Mbps"),
			UiWidgetAppearance.Read(default));

		var keys = WidgetTestSupport.CollectKeys(root).ToArray();

		// The layer, the arc, the label stack, and one node per line inside it.
		Assert.That(keys, Is.EqualTo(ExpectedTreeKeys));
	}

	[Test]
	public void The_labels_are_stacked_and_centred_so_the_value_sits_in_the_middle()
	{
		var labels = BuildLabelNode("Mbps");

		// A layer hands every child the whole box and offers no alignment of its own, so the stack
		// is what puts the block in the middle, and the lines keep value, unit, maximum order.
		Assert.That(labels.Type, Is.EqualTo("ui.stack"));
		Assert.That(labels.Properties["justify"].GetString(), Is.EqualTo("center"));
		Assert.That(labels.Properties["align"].GetString(), Is.EqualTo("center"));
		Assert.That(labels.Children.Select(child => child.Id), Is.EqualTo(ExpectedLabelLines));
	}

	[Test]
	public void An_unset_unit_leaves_the_unit_line_out()
	{
		var labels = BuildLabelNode("");

		Assert.That(labels.Children.Select(child => child.Id), Is.EqualTo(ExpectedLabelLinesWithoutUnit));
	}

	private static UiNode BuildLabelNode(string unit) =>
		UiViewBuilder.Build(
				WidgetTestSupport.WidgetSurface(),
				GaugeWidget.BuildTree(
					new UiState<double>(0.42),
					new UiState<double>(42),
					new UiState<bool>(true),
					new GaugeData("test-var", 100, unit),
					UiWidgetAppearance.Read(default)))
			.Root.Children.Single(child => child.Id == "gauge.labels");

	[Test]
	public void The_arc_falls_back_to_a_range_bar_at_the_same_level()
	{
		var root = (UiContainer)GaugeWidget.BuildTree(
			new UiState<double>(0.42),
			new UiState<double>(42),
			new UiState<bool>(true),
			new GaugeData("test-var", 100, "Mbps"),
			UiWidgetAppearance.Read(default));
		var arc = root.Children.OfType<UiGauge>().Single();

		Assert.That(arc.Fallback, Is.TypeOf<UiRangeBar>());
		Assert.That(arc.Fallback!.Key, Is.EqualTo("arc-fallback"));
	}

	[Test]
	public void A_background_colour_wraps_the_tree_in_a_modifier()
	{
		using var stored = JsonDocument.Parse("""{"backgroundColor":"#101010"}""");
		var withBackground = GaugeWidget.BuildTree(
			new UiState<double>(0.42),
			new UiState<double>(42),
			new UiState<bool>(true),
			new GaugeData("test-var", 100, ""),
			UiWidgetAppearance.Read(stored.RootElement));
		var withoutBackground = GaugeWidget.BuildTree(
			new UiState<double>(0.42),
			new UiState<double>(42),
			new UiState<bool>(true),
			new GaugeData("test-var", 100, ""),
			UiWidgetAppearance.Read(default));

		Assert.That(withBackground, Is.TypeOf<UiModifier>());
		Assert.That(withoutBackground, Is.TypeOf<UiLayer>());
	}

	[Test]
	public async Task A_live_session_emits_a_patch_when_the_value_changes()
	{
		VariableHandle[] handles =
		[
			new VariableHandle(Guid.NewGuid(), "test-var", VariableType.Numeric, 25.0, 1)
		];
		using var session = CreateSession(new GaugeData("test-var", 100, "Mbps"), new FakeVariableApi(handles));

		Assert.That(session.DrainPatches(), Is.Empty);
		await session.RefreshNowAsync();

		Assert.That(session.DrainPatches(), Is.Not.Empty);
		Assert.That(session.DrainPatches(), Is.Empty);
	}

	[Test]
	public async Task A_session_without_a_matching_variable_emits_no_patch()
	{
		using var session = CreateSession(new GaugeData("missing", 100, ""), new FakeVariableApi([]));

		await session.RefreshNowAsync();

		Assert.That(session.DrainPatches(), Is.Empty);
	}

	[Test]
	public async Task Own_hwinfo_variables_resolve_without_the_host()
	{
		using var session = CreateSession(new GaugeData("hwinfo_sensor_count", 100, ""), null);

		await session.RefreshNowAsync();

		Assert.That(session.DrainPatches(), Is.Not.Empty);
	}

	[Test]
	public void The_sample_and_the_config_session_build_trees()
	{
		using var sample = CreateSession(new GaugeData("", 100, ""), null);
		var config = new GaugeConfigSession(
			WidgetTestSupport.ConfigSurface(GaugeWidget.WidgetTypeId),
			new GaugeData("test-var", 100, "Mbps"),
			default);

		Assert.That(sample.BuildTree(), Is.Not.Null);
		Assert.That(config.BuildTree(), Is.Not.Null);
	}

	private static GaugeSession CreateSession(GaugeData data, FakeVariableApi? hostVariables) =>
		new(
			WidgetTestSupport.WidgetSurface(),
			data,
			UiWidgetAppearance.Read(default),
			WidgetTestSupport.OwnProvider(),
			hostVariables,
			new WidgetRefreshOptions(),
			WidgetTestSupport.Logger(),
			live: false);
}

/// <summary>
/// Registration and dispatch live in the integration, so the wiring is what is under test here:
/// a gauge is offered, both of its surfaces are served, and a type this plugin does not own is
/// declined rather than guessed at from the data's shape.
/// </summary>
[TestFixture]
public sealed class GaugeRegistrationTests
{
	private static readonly string[] DeclaredSurfaces =
		[UiSurfaceKinds.Widget, UiSurfaceKinds.Preview, UiSurfaceKinds.Config];

	private static readonly string[] DeclaredWidgetTypes = ["gauge"];

	private static PluginIntegration CreateIntegration() => new(WidgetTestSupport.Logger());

	[Test]
	public void The_integration_offers_the_gauge_widget_type()
	{
		var ids = CreateIntegration().GetWidgetTypes().Select(x => x.Id).ToArray();

		Assert.That(ids, Is.EqualTo(DeclaredWidgetTypes));
	}

	[Test]
	public void The_declared_surfaces_cover_widget_preview_and_config()
	{
		Assert.That(CreateIntegration().Surfaces.Select(x => x.Kind), Is.SupersetOf(DeclaredSurfaces));
	}

	[TestCase("gauge")]
	[TestCase("com.hwinfo.cjhackeryt::gauge")]
	public async Task A_gauge_widget_surface_is_served_by_the_local_or_qualified_type(string widgetType)
	{
		var session = await CreateSessionAsync(WidgetTestSupport.WidgetSurface(widgetType));

		Assert.That(session, Is.InstanceOf<GaugeSession>());
	}

	[TestCase("gauge")]
	[TestCase("com.hwinfo.cjhackeryt::gauge")]
	public async Task The_widget_picker_preview_is_served_with_a_sample(string widgetType)
	{
		var session = await CreateSessionAsync(WidgetTestSupport.PreviewSurface(widgetType));

		Assert.That(session, Is.InstanceOf<GaugeSession>());
		var value = session!.BuildTree().Root.Children
			.Single(node => node.Id == "gauge.labels")
			.Children.Single(node => node.Id == "gauge.labels.value")
			.Properties["text"]
			.GetString();

		Assert.That(value, Is.EqualTo("42.0"));
	}

	[TestCase("gauge")]
	[TestCase("com.hwinfo.cjhackeryt::gauge")]
	public async Task A_gauge_config_surface_is_served(string widgetType)
	{
		var session = await CreateSessionAsync(WidgetTestSupport.ConfigSurface(widgetType));

		Assert.That(session, Is.InstanceOf<GaugeConfigSession>());
	}

	[Test]
	public async Task A_widget_type_this_plugin_does_not_own_is_declined()
	{
		var session = await CreateSessionAsync(WidgetTestSupport.WidgetSurface("com.example.other::gauge"));

		Assert.That(session, Is.Null);
	}

	[Test]
	public async Task A_withdrawn_widget_type_is_declined()
	{
		// The speedometer type is no longer registered, so a widget still carrying it from an older
		// install keeps its stored data but is not served.
		var session = await CreateSessionAsync(WidgetTestSupport.WidgetSurface("speedometer"));

		Assert.That(session, Is.Null);
	}

	[Test]
	public async Task A_config_surface_for_another_entry_point_is_declined()
	{
		var session = await CreateSessionAsync(
			WidgetTestSupport.ConfigSurface("integration-config", GaugeWidget.WidgetTypeId));

		Assert.That(session, Is.Null);
	}

	private static async Task<IUiSession?> CreateSessionAsync(UiSurface surface) =>
		await CreateIntegration().CreateSessionAsync(WidgetTestSupport.Request(surface), CancellationToken.None);
}
