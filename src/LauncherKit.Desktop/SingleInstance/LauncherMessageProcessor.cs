using Microsoft.Extensions.Logging;

namespace LauncherKit.Desktop.SingleInstance;

/// <summary>Scoped dispatcher of consumer messages to the handlers registered for their kind.</summary>
internal sealed class LauncherMessageProcessor(
	IServiceProvider services,
	IEnumerable<LauncherMessageRegistration> registrations,
	ILogger<LauncherMessageProcessor> logger)
{
	/// <summary>Runs the registered handler; an unknown kind, invalid payload or failing handler is logged and dropped.</summary>
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
