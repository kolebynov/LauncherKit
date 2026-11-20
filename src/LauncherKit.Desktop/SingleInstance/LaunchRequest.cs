using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace LauncherKit.Desktop.SingleInstance;

/// <summary>
/// One message a relaunch sends to the owner: its <see cref="Kind"/> (the message type's <see cref="ILauncherMessage.Kind"/>)
/// selects the registered handler, which deserializes the message's JSON text in <see cref="Payload"/>.
/// </summary>
internal sealed record LaunchRequest(string Kind, string Payload)
{
	/// <summary>Serializes <paramref name="value"/> with the consumer's source-generated metadata under its <see cref="ILauncherMessage.Kind"/>.</summary>
	public static LaunchRequest Message<TMessage>(TMessage value, JsonTypeInfo<TMessage> typeInfo) where TMessage : ILauncherMessage
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(TMessage.Kind);
		ArgumentNullException.ThrowIfNull(typeInfo);
		return new LaunchRequest(TMessage.Kind, JsonSerializer.Serialize(value, typeInfo));
	}
}

[JsonSerializable(typeof(LaunchRequest))]
[JsonSourceGenerationOptions(RespectRequiredConstructorParameters = true)]
internal sealed partial class SingleInstanceJsonContext : JsonSerializerContext
{
}
