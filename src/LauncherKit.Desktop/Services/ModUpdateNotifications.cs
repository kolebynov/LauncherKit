using Avalonia.Controls.Notifications;
using LauncherKit.Core.Extensions;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Desktop.Localization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace LauncherKit.Desktop.Services;

/// <summary>
/// Shows one toast listing the installed mods that have updates (<see cref="IInstallationService.GetModInstallState"/>),
/// by display name in feed order. Scoped to a configured installation; <see cref="LauncherKitApp"/> starts it once, for the
/// scope configured at startup, when the main window opens. Disposing it (with its scope) cancels the check and drops a
/// toast not yet shown.
/// </summary>
public sealed class ModUpdateNotifications(
	IFeedProvider feedProvider,
	IModpackVersionProvider modpackVersionProvider,
	IInstallationService installationService,
	IUiDispatcher uiDispatcher,
	INotificationService notifications,
	IStringLocalizer<DesktopStrings> strings,
	ILogger<ModUpdateNotifications> logger) : IDisposable
{
	private readonly Lock _lock = new();
	private readonly CancellationTokenSource _disposeCts = new();
	private bool _disposed;
	private Task? _check;

	/// <summary>
	/// Starts the check on the first call and returns it; later calls return the same check. Never fails: errors are
	/// logged. Call on the UI thread.
	/// </summary>
	public Task Start()
	{
		// The scope may be disposed on another thread while the window opens.
		lock (_lock)
		{
			if (_check is null)
			{
				var cancellationToken = _disposed ? new CancellationToken(canceled: true) : _disposeCts.Token;
				_check = cancellationToken.IsCancellationRequested
					? Task.CompletedTask
					: Task.Run(() => Check(cancellationToken), CancellationToken.None);
			}
			return _check;
		}
	}

	public void Dispose()
	{
		lock (_lock)
		{
			if (_disposed) return;
			_disposed = true;
			_disposeCts.Cancel();
			_disposeCts.Dispose();
		}
	}

	private async Task Check(CancellationToken cancellationToken)
	{
		try
		{
			if (modpackVersionProvider.InstalledModpackVersion is null) return;

			var mods = await feedProvider.GetModFeedDatas(cancellationToken);
			var names = mods
				.Where(mod => installationService.GetModInstallState(mod).IsUpdateAvailable)
				.Select(mod => mod.ResolveDisplayName())
				.ToList();
			if (names.Count == 0) return;

			var notification = new AppNotification
			{
				Type = NotificationType.Information,
				Title = strings[DesktopStrings.ModUpdatesAvailable],
				Message = string.Join('\n', names),
			};
			uiDispatcher.Post(() =>
			{
				if (!cancellationToken.IsCancellationRequested) notifications.Show(notification);
			});
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception e)
		{
			logger.LogError(e, "Failed to check mod updates");
		}
	}
}
