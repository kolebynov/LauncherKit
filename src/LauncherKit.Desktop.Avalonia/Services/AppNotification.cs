using Avalonia.Controls.Notifications;

namespace LauncherKit.Desktop.Avalonia.Services;

/// <summary>
/// A toast shown by <see cref="INotificationService"/>. Compared by reference, so equal texts are still separate
/// notifications. The consumer's application data template for this type renders the toast content.
/// </summary>
public sealed class AppNotification
{
	public required NotificationType Type { get; init; }

	public required string Title { get; init; }

	public string? Message { get; init; }

	/// <summary>Shows an indeterminate progress bar for work that is still running.</summary>
	public bool IsInProgress { get; init; }
}
