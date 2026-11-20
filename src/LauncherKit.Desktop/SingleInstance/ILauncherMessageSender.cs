using System.Text.Json.Serialization.Metadata;

namespace LauncherKit.Desktop.SingleInstance;

/// <summary>
/// Collects the messages a relaunch sends to the running launcher, which hands each to the handler registered for its kind
/// with <see cref="LauncherKitDesktopServiceCollectionExtensions.AddLauncherMessageHandler{TMessage, THandler}"/>.
/// Delivery is one-way and best effort: nothing confirms or answers it.
/// </summary>
public interface ILauncherMessageSender
{
	/// <summary>
	/// Queues <paramref name="value"/>, serialized with the consumer's source-generated <paramref name="typeInfo"/>, after
	/// the previous messages; throws <see cref="ArgumentException"/> when it exceeds the 64 KiB frame limit.
	/// </summary>
	void Send<TMessage>(TMessage value, JsonTypeInfo<TMessage> typeInfo) where TMessage : ILauncherMessage;
}

internal sealed class LauncherMessageSender : ILauncherMessageSender
{
	private readonly List<byte[]> _frames = [];

	/// <summary>The sent messages in order.</summary>
	public IReadOnlyList<byte[]> Frames => _frames;

	public void Send<TMessage>(TMessage value, JsonTypeInfo<TMessage> typeInfo) where TMessage : ILauncherMessage =>
		_frames.Add(SingleInstanceProtocol.Serialize(LaunchRequest.Message(TMessage.Kind, value, typeInfo)));
}
