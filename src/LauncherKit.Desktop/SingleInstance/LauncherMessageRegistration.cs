using System.Text.Json;

namespace LauncherKit.Desktop.SingleInstance;

/// <summary>Pairs a message kind with the deserialization and scoped handler of its payload.</summary>
internal sealed class LauncherMessageRegistration(string kind, Action<IServiceProvider, JsonElement> handle)
{
	public string Kind { get; } = kind;

	public void Handle(IServiceProvider services, JsonElement payload) => handle(services, payload);
}
