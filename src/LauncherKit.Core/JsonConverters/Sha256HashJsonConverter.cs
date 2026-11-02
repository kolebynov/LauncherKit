using System.Text.Json;
using System.Text.Json.Serialization;
using LauncherKit.Core.Models;

namespace LauncherKit.Core.JsonConverters;

public class Sha256HashJsonConverter : JsonConverter<Sha256Hash>
{
	public override Sha256Hash Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		var hashString = reader.GetString();
		return !string.IsNullOrEmpty(hashString) ? new Sha256Hash(hashString) : throw new JsonException("Invalid hash string");
	}

	public override void Write(Utf8JsonWriter writer, Sha256Hash value, JsonSerializerOptions options)
	{
		writer.WriteStringValue(value.ToString());
	}
}