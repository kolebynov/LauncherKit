using Avalonia.Controls.Notifications;
using LauncherKit.Desktop.Services;
using LauncherKit.Desktop.SingleInstance;

namespace SampleLauncher.App;

/// <summary>Shows a toast titled <see cref="Title"/> with a later launch's working directory and arguments.</summary>
public sealed class SampleLaunchHandler(INotificationService notifications) : ILaunchHandler
{
	/// <summary>Stable title the automation flows look for.</summary>
	public const string Title = "Launched again";

	public void Handle(LaunchContext context)
	{
		// The sample has nothing to do with its own launch's arguments.
		if (context.IsFirstLaunch) return;
		notifications.Show(new AppNotification
		{
			Type = NotificationType.Information,
			Title = Title,
			Message = $"Working directory: {context.WorkingDirectory}\nArguments: "
				+ (context.Args.Count == 0 ? "(none)" : string.Join(' ', context.Args)),
		});
	}
}
