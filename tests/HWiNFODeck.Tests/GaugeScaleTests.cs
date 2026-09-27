using System.Text.Json;
using MacroDeck.Sdk.Variables;
using NUnit.Framework;

namespace HWiNFODeck.Tests;

/// <summary>
/// The contract behind the gauge widget: the stored data shape, the variable lookup that feeds it
/// and the scaling of a reading onto the arc.
/// </summary>
[TestFixture]
public sealed class GaugeScaleTests
{
	[TestCase(0, 100, 0)]
	[TestCase(50, 100, 0.5)]
	[TestCase(100, 100, 1)]
	[TestCase(200, 100, 1)]
	[TestCase(-5, 100, 0)]
	[TestCase(50, 0, 0)]
	public void Readings_scale_onto_the_arc(double value, double max, double expected)
	{
		Assert.That(GaugeScale.ToRatio(value, max), Is.EqualTo(expected).Within(0.001));
	}

	[Test]
	public void Non_finite_readings_leave_the_arc_empty()
	{
		Assert.That(GaugeScale.ToRatio(double.NaN, 100), Is.EqualTo(0));
	}

	[Test]
	public void Widget_data_parses_with_defaults()
	{
		using var full = JsonDocument.Parse("""{"variable":"hwinfo_cpu_usage","max":200.0,"unit":"%"}""");
		using var empty = JsonDocument.Parse("{}");

		Assert.That(GaugeScale.ParseData(full.RootElement), Is.EqualTo(new GaugeData("hwinfo_cpu_usage", 200, "%")));
		Assert.That(GaugeScale.ParseData(empty.RootElement), Is.EqualTo(new GaugeData("", 100, "")));
		Assert.That(GaugeScale.ParseData(default), Is.EqualTo(new GaugeData("", 100, "")));
	}

	[TestCase(0.1, 0.25)]
	[TestCase(120, 60)]
	[TestCase(2.5, 2.5)]
	[TestCase(double.NaN, 1)]
	public void Refresh_intervals_coerce_into_range(double configured, double expected)
	{
		Assert.That(GaugeScale.CoerceRefreshSeconds(configured), Is.EqualTo(expected));
		Assert.That(LiveReadingSession.RefreshPeriod(configured).TotalSeconds, Is.EqualTo(expected));
	}

	[Test]
	public void Variable_references_resolve_by_name_definition_or_qualified_id()
	{
		VariableHandle[] handles =
		[
			new VariableHandle(Guid.NewGuid(), "hwinfo_cpu_usage", VariableType.Numeric, 42.5, 2) with
			{
				DefinitionId = "hwinfo-cpu-usage"
			},
			new VariableHandle(Guid.NewGuid(), "other", VariableType.Text, "12.5", null)
		];

		Assert.That(GaugeScale.ResolveValue(handles, "hwinfo_cpu_usage"), Is.EqualTo(42.5));
		Assert.That(GaugeScale.ResolveValue(handles, "hwinfo-cpu-usage"), Is.EqualTo(42.5));
		Assert.That(GaugeScale.ResolveValue(handles, "com.cjhackeryt.hwinfodeck::hwinfo-cpu-usage"), Is.EqualTo(42.5));
		Assert.That(GaugeScale.ResolveValue(handles, "other"), Is.EqualTo(12.5));
		Assert.That(GaugeScale.ResolveValue(handles, "missing"), Is.Null);
		Assert.That(GaugeScale.ResolveValue(handles, ""), Is.Null);
	}

	[Test]
	public void Own_variable_references_map_to_local_ids()
	{
		var definitions = WidgetTestSupport.OwnProvider().Variables;

		Assert.That(GaugeScale.OwnLocalId(definitions, "hwinfo_cpu_usage"), Is.EqualTo("hwinfo-cpu-usage"));
		Assert.That(GaugeScale.OwnLocalId(definitions, "hwinfo-cpu-usage"), Is.EqualTo("hwinfo-cpu-usage"));
		Assert.That(
			GaugeScale.OwnLocalId(definitions, "com.cjhackeryt.hwinfodeck::hwinfo-cpu-usage"),
			Is.EqualTo("hwinfo-cpu-usage"));
		Assert.That(GaugeScale.OwnLocalId(definitions, "other-plugin::var"), Is.Null);
		Assert.That(GaugeScale.OwnLocalId(definitions, "missing"), Is.Null);
		Assert.That(GaugeScale.OwnLocalId(definitions, ""), Is.Null);
	}
}
