using Microsoft.Extensions.Logging;

namespace LauncherKit.Desktop.SingleInstance;

/// <inheritdoc cref="ILauncherMessageProcessor"/>
internal sealed class LauncherMessageProcessor(
	IServiceProvider services,
	IEnumerable<LauncherMessageRegistration> registrations,
	ILogger<LauncherMessageProcessor> logger) : ILauncherMessageProcessor
{
	/// <inheritdoc/>
	public void Process(string kind, string payload)
	{
		var registration = registrations.FirstOrDefault(r => r.Kind == kind);
		if (registration is null)
		{
			// The kind is untrusted text from another process, so it stays out of the logs.
			logger.LogWarning("Ignoring a launcher message of an unregistered kind");
			return;
		}

		try
		{
			registration.Handle(services, payload);
		}
		// Handlers are consumer code: one failing message must not stop the following ones.
		catch (Exception e)
		{
			logger.LogError(e, "Failed to handle a launcher message of the kind {Kind}", registration.Kind);
		}
	}
}
