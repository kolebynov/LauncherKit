using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LauncherKit.FeedEditor.Feed.Json;

/// <summary>
/// Reads and writes the launcher's <c>ModPlacement</c>: <c>{ "$type": "UnderSeparator", "Separator": "..." }</c> or
/// <c>{ "$type": "NextToMod", "ModName": "...", "Side": "Above" | "Below" }</c>, with <c>$type</c> first.
/// </summary>
internal sealed class ModPlacementJsonConverter : JsonConverter<ModPlacementModel>
{
	private const string UnderSeparator = "UnderSeparator";
	private const string NextToMod = "NextToMod";
	private const string SeparatorProperty = "Separator";
	private const string ModNameProperty = "ModName";
	private const string SideProperty = "Side";

	public override ModPlacementModel Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		var properties = TypeDiscriminator.ReadObject(ref reader, "A mod placement", out var caseName);
		return caseName switch
		{
			UnderSeparator => new ModPlacementModel
			{
				Kind = ModPlacementKind.UnderSeparator,
				Separator = ReadCaseProperties(properties, UnderSeparator, SeparatorProperty)[0],
			},
			NextToMod => ReadNextToMod(ReadCaseProperties(properties, NextToMod, ModNameProperty, SideProperty)),
			_ => throw new JsonException($"Unknown mod placement '{caseName}'."),
		};
	}

	public override void Write(Utf8JsonWriter writer, ModPlacementModel value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();
		switch (value.Kind)
		{
			case ModPlacementKind.UnderSeparator:
				writer.WriteString(TypeDiscriminator.PropertyName, UnderSeparator);
				writer.WriteString(SeparatorProperty, value.Separator);
				break;
			case ModPlacementKind.NextToMod:
				writer.WriteString(TypeDiscriminator.PropertyName, NextToMod);
				writer.WriteString(ModNameProperty, value.ModName);
				writer.WriteString(SideProperty, value.Side switch
				{
					ModListSide.Above => nameof(ModListSide.Above),
					ModListSide.Below => nameof(ModListSide.Below),
					_ => throw new ArgumentException($"Unsupported mod list side {value.Side}.", nameof(value)),
				});
				break;
			default:
				throw new ArgumentException($"Unsupported mod placement kind {value.Kind}.", nameof(value));
		}

		writer.WriteEndObject();
	}

	private static ModPlacementModel ReadNextToMod(string[] values) => new()
	{
		Kind = ModPlacementKind.NextToMod,
		ModName = values[0],
		Side = values[1] switch
		{
			nameof(ModListSide.Above) => ModListSide.Above,
			nameof(ModListSide.Below) => ModListSide.Below,
			var side => throw new JsonException($"Unknown mod list side '{side}'."),
		},
	};

	/// <summary>Reads the case's string properties, which must be exactly <paramref name="names"/>, in that order.</summary>
	private static string[] ReadCaseProperties(JsonObject properties, string caseName, params string[] names)
	{
		foreach (var (propertyName, _) in properties)
		{
			if (!names.Contains(propertyName))
			{
				throw new JsonException($"Unexpected property '{propertyName}' in \"{caseName}\".");
			}
		}

		return names
			.Select(name => properties[name] is JsonValue value && value.TryGetValue(out string? text)
				? text
				: throw new JsonException($"\"{caseName}\" requires string \"{name}\"."))
			.ToArray();
	}
}
