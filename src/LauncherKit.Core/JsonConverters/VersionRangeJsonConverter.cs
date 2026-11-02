using System.Text.Json;
using System.Text.Json.Serialization;
using NuGet.Versioning;

namespace LauncherKit.Core.JsonConverters;

internal sealed class VersionRangeJsonConverter : JsonConverter<VersionRange>
{
	public override VersionRange? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		var versionRangeString = reader.GetString();
		return VersionRange.TryParse(versionRangeString!, out var versionRange)
			? versionRange
			: throw new JsonException($"Invalid version range string {versionRangeString}");
	}

	public override void Write(Utf8JsonWriter writer, VersionRange value, JsonSerializerOptions options) =>
		writer.WriteStringValue(value.ToString());
}