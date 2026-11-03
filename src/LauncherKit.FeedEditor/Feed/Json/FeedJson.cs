using System.Collections;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace LauncherKit.FeedEditor.Feed.Json;

public static class FeedJson
{
	/// <summary>Relaxed escaping keeps Cyrillic display names and apostrophes readable in the stored file.</summary>
	private static readonly JsonSerializerOptions Options = new(FeedJsonContext.Default.Options)
	{
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		TypeInfoResolver = FeedJsonContext.Default.WithAddedModifier(OmitEmptyCollections),
	};

	/// <exception cref="JsonException">The JSON is not a valid feed.</exception>
	public static FeedModel Deserialize(string json) =>
		JsonSerializer.Deserialize(json, Options.GetTypeInfo<FeedModel>())
		?? throw new JsonException("The feed must be a JSON object.");

	public static string Serialize(FeedModel feed) => JsonSerializer.Serialize(feed, Options.GetTypeInfo<FeedModel>());

	/// <summary>Deep-copies any feed object through its JSON form, so the copy is exactly what would be saved.</summary>
	public static T Clone<T>(T value)
		where T : class
	{
		var typeInfo = Options.GetTypeInfo<T>();
		return JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(value, typeInfo), typeInfo)!;
	}

	private static void OmitEmptyCollections(JsonTypeInfo typeInfo)
	{
		foreach (var property in typeInfo.Properties)
		{
			if (property.AttributeProvider?.IsDefined(typeof(OmitWhenEmptyAttribute), inherit: false) == true)
			{
				property.ShouldSerialize = static (_, value) => value is ICollection { Count: > 0 };
			}
		}
	}
}
