using MacroDeck.Plugin.Testing;
using MacroDeck.Sdk.ConfigFlow;
using NUnit.Framework;
using Serilog;

namespace HWiNFODeck.Tests;

/// <summary>
/// Behaviour tests through <see cref="PluginTestHarness"/>: the plugin's own capability handlers run,
/// but nothing crosses a socket. This is where you test what your integration does.
/// </summary>
[TestFixture]
public sealed class PluginIntegrationTests
{
	private static PluginTestHarness CreateHarness() =>
		PluginTestHarness.Create(builder => builder
			.UseLocalization(Strings.LocalizationCatalog)
			.RegisterIntegration<PluginIntegration>());

	[Test]
	public async Task The_plugin_builds_and_initializes()
	{
		await using var harness = CreateHarness();

		Assert.DoesNotThrowAsync(harness.InitializeIntegrationsAsync);
	}

}

/// <summary>
/// The localization set is generated from <c>Localization/*.resx</c>, so these guard the wiring rather
/// than any wording: a missing catalog registration leaves every label showing its raw key.
/// </summary>
[TestFixture]
public sealed class LocalizationTests
{
	[Test]
	public void The_catalog_is_scoped_to_the_plugin_id()
	{
		Assert.That(Strings.LocalizationCatalog.Scope, Is.EqualTo("plugin:com.cjhackeryt.hwinfodeck"));
	}

	[Test]
	public void English_is_the_default_culture()
	{
		Assert.That(Strings.LocalizationCatalog.DefaultCulture, Is.EqualTo("en"));
		Assert.That(Strings.LocalizationCatalog.Cultures, Does.Contain("en"));
	}

	[Test]
	public void The_plugin_strings_come_from_the_catalog()
	{
		Assert.That(Strings.LocalizationCatalog.KeysOf("en"), Does.Contain("HWiNFO.Description"));
	}

	[Test]
	public void Every_key_the_default_culture_declares_resolves_to_text()
	{
		foreach (var key in Strings.LocalizationCatalog.KeysOf("en"))
		{
			Assert.That(Strings.LocalizationCatalog.TryGetTemplate("en", key, out var text), Is.True);
			Assert.That(text, Is.Not.Empty);
		}
	}
}

/// <summary>
/// The variable provider's fixed catalog: the ids are part of the plugin's public surface
/// (user data binds to them), so a rename or removal must fail a test.
/// </summary>
[TestFixture]
public sealed class VariableProviderTests
{
	private static readonly string[] PluginVersionVariables = ["macrodeck-version", "macrodeck-sdk-version"];

	private static readonly string[] NetworkAndDriveVariables =
	[
		"hwinfo-network-download-speed-mbps",
		"hwinfo-network-download-speed-mb-s",
		"hwinfo-network-upload-speed-mbps",
		"hwinfo-network-upload-speed-mb-s",
		"hwinfo-drive-used-percent",
		"hwinfo-drive-free-space-gb"
	];

	private static readonly string[] GpuAndVramVariables =
	[
		"hwinfo-gpu-usage-percent",
		"hwinfo-gpu-temperature",
		"hwinfo-gpu-clock",
		"hwinfo-gpu-power",
		"hwinfo-vram-used-mb",
		"hwinfo-vram-usage-percent"
	];

	private static HWiNFOVariableProvider CreateProvider() =>
		new(new HWiNFOSharedMemoryService(new LoggerConfiguration().CreateLogger()),
			new LoggerConfiguration().CreateLogger());

	[Test]
	public void The_catalog_declares_the_plugin_version_variables()
	{
		Assert.That(
			CreateProvider().Variables.Select(v => v.Id),
			Is.SupersetOf(PluginVersionVariables));
	}

	[Test]
	public void The_catalog_declares_the_network_and_drive_variables()
	{
		Assert.That(
			CreateProvider().Variables.Select(v => v.Id),
			Is.SupersetOf(NetworkAndDriveVariables));
	}

	[Test]
	public void The_catalog_declares_gpu_and_vram_variables()
	{
		Assert.That(
			CreateProvider().Variables.Select(v => v.Id),
			Is.SupersetOf(GpuAndVramVariables));
	}

	[Test]
	public void A_sensor_catalog_entry_has_a_stable_local_id_and_readable_name()
	{
		var definition = HWiNFOVariableProvider.CreateSensorDefinition(
			new HWiNFOSensor(12, "NVIDIA GeForce RTX", 44, "GPU Memory Used", "MB", 512));

		Assert.That(definition.Id, Is.EqualTo("sensor-12-44"));
		Assert.That(definition.Name, Is.EqualTo("sensor_12_44"));
		Assert.That(definition.DisplayName.Literal, Is.EqualTo("NVIDIA GeForce RTX: GPU Memory Used"));
	}

	[Test]
	public async Task The_sdk_version_variable_reports_the_built_sdk_version()
	{
		var provider = CreateProvider();

		var reading = await provider.ReadAsync("macrodeck-sdk-version", CancellationToken.None);

		Assert.That(reading.Value is null, Is.False);
		Assert.That(reading.Value, Is.EqualTo(MacroDeckHostInfo.SdkVersion));
		Assert.That(reading.Value!.ToString(), Does.StartWith("3.0.0"));
	}
}

[TestFixture]
public sealed class NetworkSpeedConverterTests
{
	[TestCase(1024.0, "KB/s", 1.0, 8.0)]
	[TestCase(512.0, "kB/s", 0.5, 4.0)]
	[TestCase(2000.0, "MB/s", 2000.0, 16000.0)]
	[TestCase(2.0, "GB/s", 2048.0, 16384.0)]
	[TestCase(1000.0, "Mbps", 125.0, 1000.0)]
	[TestCase(0.0, "B/s", 0.0, 0.0)]
	public void Speed_readings_convert_to_both_rate_units(double value, string unit, double mbPerSecond, double mbps)
	{
		var reading = new HWiNFOSensor(1, "Test NIC", 1, "Download Speed", unit, value);

		Assert.That(NetworkSpeedConverter.ToMegabytesPerSecond(reading), Is.EqualTo(mbPerSecond).Within(0.001));
		Assert.That(NetworkSpeedConverter.ToMegabitsPerSecond(reading), Is.EqualTo(mbps).Within(0.001));
	}

	[Test]
	public void Unsupported_units_are_treated_as_unavailable()
	{
		var reading = new HWiNFOSensor(1, "Test NIC", 1, "Download Speed", "%", 50);

		Assert.That(NetworkSpeedConverter.ToMegabytesPerSecond(reading), Is.Null);
		Assert.That(NetworkSpeedConverter.ToMegabitsPerSecond(reading), Is.Null);
	}

	[Test]
	public void Missing_readings_are_treated_as_unavailable()
	{
		Assert.That(NetworkSpeedConverter.ToMegabytesPerSecond(null), Is.Null);
		Assert.That(NetworkSpeedConverter.ToMegabitsPerSecond(null), Is.Null);
	}
}

[TestFixture]
public sealed class SensorAliasesTests
{
	private static HWiNFOSensor Reading(string sensor, string name, string unit, double value) =>
		new(1, sensor, 1, name, unit, value);

	[Test]
	public void Dl_and_ul_rate_labels_resolve_to_the_matching_direction()
	{
		HWiNFOSensor[] readings =
		[
			Reading("Intel Wi-Fi 6 AX201", "Current DL Rate", "kB/s", 1024),
			Reading("Intel Wi-Fi 6 AX201", "Current UL Rate", "kB/s", 512)
		];

		var download = SensorAliases.FindDownloadReading(readings);
		var upload = SensorAliases.FindUploadReading(readings);

		Assert.That(download?.ReadingName, Is.EqualTo("Current DL Rate"));
		Assert.That(upload?.ReadingName, Is.EqualTo("Current UL Rate"));
		Assert.That(NetworkSpeedConverter.ToMegabitsPerSecond(download), Is.EqualTo(8).Within(0.001));
	}

	[Test]
	public void Download_speed_labels_still_resolve()
	{
		HWiNFOSensor[] readings =
		[
			Reading("Realtek PCIe GbE Family Controller", "Download Speed", "MB/s", 12.5),
			Reading("Realtek PCIe GbE Family Controller", "Upload Speed", "MB/s", 3.25)
		];

		Assert.That(SensorAliases.FindDownloadReading(readings)?.Value, Is.EqualTo(12.5));
		Assert.That(SensorAliases.FindUploadReading(readings)?.Value, Is.EqualTo(3.25));
	}

	[Test]
	public void Cumulative_totals_are_never_mistaken_for_speeds()
	{
		HWiNFOSensor[] readings =
		[
			Reading("Intel Wi-Fi 6 AX201", "Downloaded Total", "GB", 41.2),
			Reading("Intel Wi-Fi 6 AX201", "Uploaded Total", "GB", 5.7)
		];

		Assert.That(SensorAliases.FindDownloadReading(readings), Is.Null);
		Assert.That(SensorAliases.FindUploadReading(readings), Is.Null);
	}

	[Test]
	public void Unlabeled_nic_rates_fall_back_to_the_nic_sensor()
	{
		HWiNFOSensor[] readings =
		[
			Reading("Killer E2600 Gigabit Ethernet", "Rate", "kB/s", 2048)
		];

		Assert.That(SensorAliases.FindDownloadReading(readings)?.Value, Is.EqualTo(2048));
	}

	[Test]
	public void Drive_used_percent_matches_drive_scoped_readings_only()
	{
		HWiNFOSensor[] readings =
		[
			Reading("Physical Memory", "Memory Used %", "%", 62),
			Reading("S.M.A.R.T.: KINGSTON SNV2S1000G", "Drive Used %", "%", 37)
		];

		Assert.That(SensorAliases.FindDriveUsedPercent(readings), Is.EqualTo(37));
	}

	[Test]
	public void Drive_free_space_converts_terabytes_to_gigabytes()
	{
		HWiNFOSensor[] readings =
		[
			Reading("S.M.A.R.T.: WDC WD40EFRX", "Drive Free Space", "TB", 1.5)
		];

		Assert.That(SensorAliases.FindDriveFreeGigabytes(readings), Is.EqualTo(1536));
	}

	[Test]
	public void Gpu_aliases_match_graphics_readings()
	{
		HWiNFOSensor[] readings =
		[
			Reading("NVIDIA GeForce RTX 4090", "GPU Core Load", "%", 73),
			Reading("NVIDIA GeForce RTX 4090", "GPU Temperature", "°C", 68),
			Reading("NVIDIA GeForce RTX 4090", "GPU Clock", "MHz", 2100),
			Reading("NVIDIA GeForce RTX 4090", "GPU Power", "W", 320)
		];

		Assert.That(SensorAliases.FindGpuUsage(readings)?.Value, Is.EqualTo(73));
		Assert.That(SensorAliases.FindGpuTemperature(readings)?.Value, Is.EqualTo(68));
		Assert.That(SensorAliases.FindGpuClock(readings)?.Value, Is.EqualTo(2100));
		Assert.That(SensorAliases.FindGpuPower(readings)?.Value, Is.EqualTo(320));
	}

	[Test]
	public void Vram_aliases_find_memory_usage_and_convert_units()
	{
		HWiNFOSensor[] readings =
		[
			Reading("NVIDIA GeForce RTX 4090", "GPU Memory Usage", "%", 42),
			Reading("NVIDIA GeForce RTX 4090", "Dedicated GPU Memory Used", "GB", 4)
		];

		Assert.That(SensorAliases.FindVramUsagePercent(readings)?.Value, Is.EqualTo(42));
		Assert.That(SensorAliases.FindVramUsedMegabytes(readings), Is.EqualTo(4096));
	}

	[Test]
	public void Drive_lookups_return_null_without_matching_readings()
	{
		HWiNFOSensor[] readings =
		[
			Reading("S.M.A.R.T.: KINGSTON SNV2S1000G", "Drive Temperature", "°C", 41)
		];

		Assert.That(SensorAliases.FindDriveUsedPercent(readings), Is.Null);
		Assert.That(SensorAliases.FindDriveFreeGigabytes(readings), Is.Null);
	}

	[Test]
	public void The_fixed_drive_fallback_reports_plausible_ranges()
	{
		var fixedDrive = SensorAliases.ReadFixedDrive();

		Assert.That(fixedDrive is null, Is.False);
		Assert.That(fixedDrive!.Value.UsedPercent, Is.InRange(0, 100));
		Assert.That(fixedDrive.Value.FreeGigabytes, Is.GreaterThanOrEqualTo(0));
	}
}
/// <summary>
/// The plugin's one config flow. It owns the refresh interval every gauge widget polls at, so the
/// stored key is part of the plugin's user data and must not be renamed.
/// </summary>
[TestFixture]
public sealed class PluginConfigFlowTests
{
	[Test]
	public async Task The_config_flow_completes_with_a_valid_interval()
	{
		var flow = new PluginConfigFlow();

		var start = await flow.StartAsync(null!, CancellationToken.None);
		var completed = await flow.SubmitAsync(
			PluginConfigFlow.StepId,
			new Dictionary<string, object?> { [PluginConfigFlow.RefreshKey] = 2.5 },
			null!,
			CancellationToken.None);

		Assert.That(start.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
		Assert.That(completed.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
		Assert.That(completed.Values, Is.Not.Null);
		Assert.That(completed.Values!.TryGetValue(PluginConfigFlow.RefreshKey, out var stored), Is.True);
		Assert.That(stored!.Value, Is.EqualTo("2.5"));
	}

	[TestCase(0.1)]
	[TestCase(120)]
	[TestCase("fast")]
	public async Task The_config_flow_rejects_an_invalid_interval(object? raw)
	{
		var flow = new PluginConfigFlow();

		var rejected = await flow.SubmitAsync(
			PluginConfigFlow.StepId,
			new Dictionary<string, object?> { [PluginConfigFlow.RefreshKey] = raw },
			null!,
			CancellationToken.None);

		Assert.That(rejected.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
	}
}