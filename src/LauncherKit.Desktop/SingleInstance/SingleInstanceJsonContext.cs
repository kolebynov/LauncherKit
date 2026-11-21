using System.Text.Json.Serialization;

namespace LauncherKit.Desktop.SingleInstance;

[JsonSerializable(typeof(LaunchContext))]
[JsonSourceGenerationOptions(RespectRequiredConstructorParameters = true)]
internal sealed partial class SingleInstanceJsonContext : JsonSerializerContext
{
}
