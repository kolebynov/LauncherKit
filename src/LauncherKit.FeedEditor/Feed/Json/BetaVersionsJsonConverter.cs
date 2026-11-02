using System.Text.Json;
using System.Text.Json.Serialization;

namespace LauncherKit.FeedEditor.Feed.Json;

/// <summary>Maps the feed's <c>{ "hash": "range" }</c> object to an ordered, editable list.</summary>
internal sealed class BetaVersionsJsonConverter : JsonConverter<List<BetaVersionModel>>
{
	public override List<BetaVersionModel> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType != JsonTokenType.StartObject)
		{
			throw new JsonException("\"BetaVersions\" must be an object.");
		}

		var betaVersions = new List<BetaVersionModel>();
		while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
		{
			var hash = reader.GetString()!;
			reader.Read();
			var versionRange = reader.TokenType == JsonTokenType.String
				? reader.GetString()!
				: throw new JsonException($"Beta version range for '{hash}' must be a string.");
			betaVersions.Add(new BetaVersionModel { Hash = hash, VersionRange = versionRange });
		}

		return betaVersions;
	}

	public override void Write(Utf8JsonWriter writer, List<BetaVersionModel> value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();
		foreach (var betaVersion in value)
		{
			writer.WriteString(betaVersion.Hash, betaVersion.VersionRange);
		}

		writer.WriteEndObject();
	}
}
