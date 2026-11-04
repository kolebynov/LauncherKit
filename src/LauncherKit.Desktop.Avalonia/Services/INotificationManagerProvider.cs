using Avalonia.Controls.Notifications;

namespace LauncherKit.Desktop.Avalonia.Services;

/// <summary>
/// Supplies the manager <see cref="INotificationService"/> shows toasts in, typically the main window's. Registered by
/// the consumer; it is resolved on the first notification, so creating the service does not create the window.
/// </summary>
public interface INotificationManagerProvider
{
	/// <summary>
	/// Create it with its window rather than on first use: until the manager's own template is applied, Show adds the
	/// card to a placeholder list that is then discarded, so the first notification would never appear.
	/// </summary>
	WindowNotificationManager NotificationManager { get; }
}
