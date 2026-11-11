using System.Text.Json.Serialization;

namespace LauncherKit.FeedEditor.Feed.Json;

[JsonSerializable(typeof(FeedModel))]
[JsonSerializable(typeof(InstallModAction))]
[JsonSerializable(typeof(SelectRequiredModAction))]
[JsonSerializable(typeof(SelectChoicesAction))]
[JsonSerializable(typeof(ActivateModsAction))]
[JsonSerializable(typeof(ActivatePluginsAction))]
[JsonSerializable(typeof(RemoveFileSystemItemAction))]
[JsonSerializable(typeof(ExtractArchiveAction))]
[JsonSerializable(typeof(MergePluginsAction))]
[JsonSerializable(typeof(ChangeProfileAction))]
[JsonSourceGenerationOptions(
	Converters = [typeof(ModInstallActionJsonConverter)],
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	UseStringEnumConverter = true,
	WriteIndented = true,
	NewLine = "\n")]
internal sealed partial class FeedJsonContext : JsonSerializerContext;
