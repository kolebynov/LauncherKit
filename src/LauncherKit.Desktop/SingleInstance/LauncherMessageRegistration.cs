namespace LauncherKit.Desktop.SingleInstance;

/// <summary>Pairs a message kind with the deserialization and scoped handler of its JSON payload.</summary>
internal sealed class LauncherMessageRegistration(string kind, Action<IServiceProvider, string> handle)
{
	public string Kind { get; } = kind;

	public void Handle(IServiceProvider services, string payload) => handle(services, payload);
}
