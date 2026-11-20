namespace LauncherKit.Desktop.SingleInstance;

/// <summary>Scoped dispatcher of consumer messages to the handlers registered for their kind.</summary>
internal interface ILauncherMessageProcessor
{
	/// <summary>Runs the registered handler; an unknown kind, invalid payload or failing handler is logged and dropped.</summary>
	void Process(string kind, string payload);
}
