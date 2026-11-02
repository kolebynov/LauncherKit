using System.Text.Json.Serialization;

namespace LauncherKit.Core.OAuth;

[JsonSerializable(typeof(OAuthToken))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class OAuthJsonContext : JsonSerializerContext
{
}
