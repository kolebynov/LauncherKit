using Avalonia.Controls.Notifications;
using Avalonia.Threading;

namespace LauncherKit.Desktop.Services;

internal sealed class NotificationService : INotificationService
{
	private readonly Func<WindowNotificationManager>? _getManager;

	/// <param name="getManager">
	/// The manager to show toasts in, resolved on first use; <see langword="null"/> when the consumer registered no
	/// <see cref="INotificationManagerProvider"/>, so notifications are not shown.
	/// </param>
	public NotificationService(Func<WindowNotificationManager>? getManager = null)
	{
		_getManager = getManager;
	}

	public void Show(AppNotification notification)
	{
		Dispatcher.UIThread.VerifyAccess();
		// The consumer's data template renders the notification; a zero expiration keeps it until it is closed.
		_getManager?.Invoke().Show(notification, notification.Type, TimeSpan.Zero);
	}

	public void Close(AppNotification notification)
	{
		Dispatcher.UIThread.VerifyAccess();
		_getManager?.Invoke().Close(notification);
	}
}
