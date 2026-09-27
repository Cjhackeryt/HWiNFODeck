using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Model.Versioning;
using Serilog;

namespace HWiNFODeck.Tests;

/// <summary>
/// <summary>
/// Fixtures shared across the widget tests, so they do not each grow their own copies of the same
/// surface builders, key walker and variable stub.
/// </summary>
/// </summary>
internal static class WidgetTestSupport
{
	public static UiSurface WidgetSurface(string? widgetType = null) => new()
	{
		Kind = UiSurfaceKinds.Widget,
		SessionMode = UiSessionModes.Shared,
		Attributes = widgetType is null
			? new Dictionary<string, JsonElement>()
			: Attributes((UiWidgetSurfaceAttributes.WidgetType, widgetType))
	};

	public static UiSurface PreviewSurface(string widgetType)
	{
		var attributes = Attributes((UiWidgetSurfaceAttributes.WidgetType, widgetType));
		attributes[UiWidgetSurfaceAttributes.Sample] = JsonSerializer.SerializeToElement(true);
		return new UiSurface
		{
			Kind = UiSurfaceKinds.Preview,
			SessionMode = UiSessionModes.Shared,
			Attributes = attributes
		};
	}

	public static UiSurface ConfigSurface(string widgetType) =>
		ConfigSurface(UiConfigEntryPoints.WidgetConfig, widgetType);

	public static UiSurface ConfigSurface(string entryPoint, string widgetType) => new()
	{
		Kind = UiSurfaceKinds.Config,
		SessionMode = UiSessionModes.Exclusive,
		Attributes = Attributes(
			(UiConfigSurfaceAttributes.EntryPoint, entryPoint),
			(UiConfigSurfaceAttributes.WidgetType, widgetType))
	};

	public static UiSessionRequest Request(UiSurface surface) => new()
	{
		Surface = surface,
		UiModelVersion = UiModelVersions.Current
	};

	public static HWiNFOVariableProvider OwnProvider() =>
		new(new HWiNFOSharedMemoryService(new LoggerConfiguration().CreateLogger()),
			new LoggerConfiguration().CreateLogger());

	public static ILogger Logger() => new LoggerConfiguration().CreateLogger();

	public static IEnumerable<string> CollectKeys(UiElement element)
	{
		yield return element.Key;
		if (element is UiContainer container)
			foreach (var child in container.Children)
				foreach (var key in CollectKeys(child))
					yield return key;
	}

	private static Dictionary<string, JsonElement> Attributes(params (string Key, string Value)[] entries)
	{
		var attributes = new Dictionary<string, JsonElement>();
		foreach (var (key, value) in entries)
		{
			using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
			attributes[key] = document.RootElement.Clone();
		}

		return attributes;
	}
}

internal sealed class FakeVariableApi : IVariableApi
{
	private readonly IReadOnlyList<VariableHandle> _handles;

	public FakeVariableApi(IReadOnlyList<VariableHandle> handles) => _handles = handles;

	public Task<IReadOnlyList<VariableHandle>> GetAllAsync() => Task.FromResult(_handles);

	public Task<VariableHandle?> GetByNameAsync(string name) => throw new NotImplementedException();
	public Task<VariableHandle> CreateAsync(string name, VariableType type, object? initialValue = null, int? decimalPlaces = null, string? definitionId = null) => throw new NotImplementedException();
	public Task<VariableHandle> CreateAsync(VariableDefinition declaration, object? initialValue = null) => throw new NotImplementedException();
	public Task DeleteAsync(Guid id) => throw new NotImplementedException();
	public Task SetValueAsync(Guid variableId, object? value) => throw new NotImplementedException();
}
