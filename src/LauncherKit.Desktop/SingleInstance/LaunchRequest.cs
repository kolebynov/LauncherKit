using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace LauncherKit.Desktop.SingleInstance;

/// <summary>
/// One message a relaunch sends to the owner: its <see cref="Kind"/> (the message type's <see cref="ILauncherMessage.Kind"/>)
/// selects the registered handler, which deserializes <see cref="Payload"/>.
/// </summary>
internal sealed record LaunchRequest(int Version, string Kind, JsonElement Payload)
{
	public const int CurrentVersion = 1;

	/// <summary>Serializes <paramref name="value"/> with the consumer's source-generated metadata.</summary>
	public static LaunchRequest Message<TMessage>(string kind, TMessage value, JsonTypeInfo<TMessage> typeInfo)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(kind);
		ArgumentNullException.ThrowIfNull(typeInfo);
		return new LaunchRequest(CurrentVersion, kind, JsonSerializer.SerializeToElement(value, typeInfo));
	}
}

[JsonSerializable(typeof(LaunchRequest))]
[JsonSourceGenerationOptions(RespectRequiredConstructorParameters = true)]
internal sealed partial class SingleInstanceJsonContext : JsonSerializerContext
{
}
