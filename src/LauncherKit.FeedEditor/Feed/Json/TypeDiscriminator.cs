using System.Text.Json;
using System.Text.Json.Nodes;

namespace LauncherKit.FeedEditor.Feed.Json;

/// <summary>
/// The launcher's closed-hierarchy format for feed cases: an object whose first property, <c>$type</c>, is the case
/// type name, followed by the case properties.
/// </summary>
internal static class TypeDiscriminator
{
	public const string PropertyName = "$type";

	/// <summary>Reads an object and returns its properties without <c>$type</c>, which must come first.</summary>
	public static JsonObject ReadObject(ref Utf8JsonReader reader, string description, out string caseName)
	{
		if (reader.TokenType != JsonTokenType.StartObject || JsonNode.Parse(ref reader) is not JsonObject properties)
		{
			throw new JsonException($"{description} must be an object.");
		}

		if (properties.Count == 0
			|| properties.GetAt(0) is not (PropertyName, JsonValue type)
			|| !type.TryGetValue(out string? name))
		{
			throw new JsonException($"{description} must start with a string \"{PropertyName}\" property.");
		}

		properties.RemoveAt(0);
		caseName = name;
		return properties;
	}
}
