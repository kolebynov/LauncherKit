using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LauncherKit.FeedEditor.Feed.Json;

/// <summary>
/// Reads and writes <see cref="ModInstallActionModel"/> in the launcher's <c>InstallAction</c> format:
/// <c>{ "$type": "CaseName", ...case properties... }</c>, with <c>$type</c> first.
/// </summary>
internal sealed class ModInstallActionJsonConverter : JsonConverter<ModInstallActionModel>
{
	public override ModInstallActionModel Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		var properties = TypeDiscriminator.ReadObject(ref reader, "A mod install action", out var caseName);
		return caseName switch
		{
			"InstallMod" => ReadCase<InstallModAction>(properties, options),
			"SelectRequiredMod" => ReadCase<SelectRequiredModAction>(properties, options),
			"ActivateMods" => ReadCase<ActivateModsAction>(properties, options),
			"ActivatePlugins" => ReadCase<ActivatePluginsAction>(properties, options),
			"RemoveFileSystemItem" => ReadCase<RemoveFileSystemItemAction>(properties, options),
			"ExtractArchive" => ReadCase<ExtractArchiveAction>(properties, options),
			"MergePlugins" => ReadCase<MergePluginsAction>(properties, options),
			"ChangeProfile" => ReadCase<ChangeProfileAction>(properties, options),
			_ => throw new JsonException($"Unknown mod install action '{caseName}'."),
		};
	}

	public override void Write(Utf8JsonWriter writer, ModInstallActionModel value, JsonSerializerOptions options)
	{
		switch (value)
		{
			case InstallModAction action:
				WriteCase(writer, "InstallMod", action, options);
				break;
			case SelectRequiredModAction action:
				WriteCase(writer, "SelectRequiredMod", action, options);
				break;
			case ActivateModsAction action:
				WriteCase(writer, "ActivateMods", action, options);
				break;
			case ActivatePluginsAction action:
				WriteCase(writer, "ActivatePlugins", action, options);
				break;
			case RemoveFileSystemItemAction action:
				WriteCase(writer, "RemoveFileSystemItem", action, options);
				break;
			case ExtractArchiveAction action:
				WriteCase(writer, "ExtractArchive", action, options);
				break;
			case MergePluginsAction action:
				WriteCase(writer, "MergePlugins", action, options);
				break;
			case ChangeProfileAction action:
				WriteCase(writer, "ChangeProfile", action, options);
				break;
			default:
				throw new ArgumentException($"Unsupported mod install action type {value.GetType()}.", nameof(value));
		}
	}

	private static T ReadCase<T>(JsonObject properties, JsonSerializerOptions options)
		where T : ModInstallActionModel =>
		properties.Deserialize(options.GetTypeInfo<T>())
		?? throw new JsonException($"Mod install action {typeof(T).Name} must not be null.");

	private static void WriteCase<T>(Utf8JsonWriter writer, string caseName, T action, JsonSerializerOptions options)
		where T : ModInstallActionModel
	{
		writer.WriteStartObject();
		writer.WriteString(TypeDiscriminator.PropertyName, caseName);
		foreach (var property in JsonSerializer.SerializeToElement(action, options.GetTypeInfo<T>()).EnumerateObject())
		{
			property.WriteTo(writer);
		}

		writer.WriteEndObject();
	}
}
