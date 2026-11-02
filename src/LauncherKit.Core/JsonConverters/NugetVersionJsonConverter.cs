using System.Text.Json;
using System.Text.Json.Serialization;
using NuGet.Versioning;

namespace LauncherKit.Core.JsonConverters;

public sealed class NugetVersionJsonConverter : JsonConverter<NuGetVersion>
{
	public override NuGetVersion? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		var versionString = reader.GetString();
		return NuGetVersion.TryParse(versionString, out var version) ? version : throw new JsonException($"Invalid version string {versionString}");
	}

	public override void Write(Utf8JsonWriter writer, NuGetVersion value, JsonSerializerOptions options) =>
		writer.WriteStringValue(value.ToString());
}