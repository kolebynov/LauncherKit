using System.Text.Json.Serialization;

namespace LauncherKit.Core.Analytics;

[JsonSerializable(typeof(InstallationEvent))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class AnalyticsJsonContext : JsonSerializerContext
{
}
