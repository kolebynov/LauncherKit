using Avalonia.Controls.Notifications;
using LauncherKit.Core.Initialization;
using LauncherKit.Desktop.Services;
using LauncherKit.Desktop.Localization;
using LauncherKit.Desktop.ViewModels;
using Microsoft.Extensions.Localization;
using R3;

namespace LauncherKit.Desktop.Initialization;

/// <summary>
/// Shows the delayed feed load status as a notification: a progress toast while the feed loads, replaced by an error
/// toast when the load fails and closed when it succeeds.
/// </summary>
public sealed class FeedLoadNotificationsInitializer(
	FeedLoadStatusViewModel feedStatus,
	INotificationService notifications,
	IStringLocalizer<DesktopStrings> strings,
	ExceptionTexts exceptionTexts) : IGlobalInitializer, IDisposable
{
	private IDisposable? _subscription;
	private AppNotification? _notification;

	public ValueTask Initialize()
	{
		_subscription ??= feedStatus.ObservePropertyChanged(x => x.Status).Subscribe(status =>
		{
			if (_notification is not null) notifications.Close(_notification);
			_notification = status switch
			{
				FeedLoadStatus.Idle => null,
				FeedLoadStatus.Loading => new AppNotification
				{
					Type = NotificationType.Information,
					Title = strings[DesktopStrings.FeedLoading],
					IsInProgress = true,
				},
				FeedLoadStatus.Failed(var error) => new AppNotification
				{
					Type = NotificationType.Error,
					Title = strings[DesktopStrings.FeedLoadFailed],
					Message = exceptionTexts.Get(error) + "\n" + strings[DesktopStrings.FeedUnavailableFeatures],
				},
			};
			if (_notification is not null) notifications.Show(_notification);
		});
		return ValueTask.CompletedTask;
	}

	public void Dispose() => _subscription?.Dispose();
}
