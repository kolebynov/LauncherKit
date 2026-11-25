using System.Text.Json;
using System.Text.Json.Serialization;

namespace LauncherKit.FeedEditor.Feed.Json;

/// <summary>
/// Reads and writes <see cref="ModDependencyModel"/> in the launcher's <c>ModDependency</c> format:
/// <c>{ "$type": "AnotherMod", "Mods": [...] }</c>, with <c>$type</c> first.
/// </summary>
internal sealed class ModDependencyJsonConverter : JsonConverter<ModDependencyModel>
{
	private const string AnotherMod = "AnotherMod";

	public override ModDependencyModel Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		var properties = TypeDiscriminator.ReadObject(ref reader, "A mod dependency", out var caseName);
		return caseName switch
		{
			AnotherMod => properties.Deserialize(options.GetTypeInfo<AnotherModDependency>())
				?? throw new JsonException("Mod dependency AnotherMod must not be null."),
			_ => throw new JsonException($"Unknown mod dependency '{caseName}'."),
		};
	}

	public override void Write(Utf8JsonWriter writer, ModDependencyModel value, JsonSerializerOptions options)
	{
		switch (value)
		{
			case AnotherModDependency dependency:
				writer.WriteStartObject();
				writer.WriteString(TypeDiscriminator.PropertyName, AnotherMod);
				foreach (var property in JsonSerializer.SerializeToElement(dependency, options.GetTypeInfo<AnotherModDependency>()).EnumerateObject())
				{
					property.WriteTo(writer);
				}

				writer.WriteEndObject();
				break;
			default:
				throw new ArgumentException($"Unsupported mod dependency type {value.GetType()}.", nameof(value));
		}
	}
}
