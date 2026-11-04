namespace LauncherKit.Desktop.Avalonia.Services;

/// <summary>
/// Toasts of the consumer's <see cref="INotificationManagerProvider"/>. A toast stays until the user clicks it or its
/// owner closes it. Without a registered provider notifications are not shown. Must be used on the UI thread.
/// </summary>
public interface INotificationService
{
	void Show(AppNotification notification);

	/// <summary>Closes <paramref name="notification"/>; does nothing when it is not shown.</summary>
	void Close(AppNotification notification);
}
