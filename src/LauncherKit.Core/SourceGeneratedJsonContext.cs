using System.Text.Json.Serialization;
using LauncherKit.Core.Feed;
using LauncherKit.Core.JsonConverters;

namespace LauncherKit.Core;

[JsonSerializable(typeof(FeedData))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
[JsonSerializable(typeof(string[]))]
[JsonSourceGenerationOptions(
	Converters = [typeof(NugetVersionJsonConverter), typeof(VersionRangeJsonConverter), typeof(Sha256HashJsonConverter)],
	UseStringEnumConverter = true,
	WriteIndented = true,
	RespectRequiredConstructorParameters = true)]
public sealed partial class SourceGeneratedJsonContext : JsonSerializerContext
{
}
