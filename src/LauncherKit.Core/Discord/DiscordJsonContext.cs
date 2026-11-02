using System.Text.Json.Serialization;

namespace LauncherKit.Core.Discord;

[JsonSerializable(typeof(DiscordUserResponse))]
[JsonSerializable(typeof(DiscordGuildMemberResponse))]
[JsonSourceGenerationOptions(
	PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
	NumberHandling = JsonNumberHandling.AllowReadingFromString)]
internal sealed partial class DiscordJsonContext : JsonSerializerContext
{
}
