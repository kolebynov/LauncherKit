namespace LauncherKit.Desktop.Avalonia.Services;

/// <summary>
/// Dialogs the kit shows itself (e.g. <see cref="MainWindowCloseHandler"/>'s confirmation). The kit registers no
/// implementation: the consumer implements and registers it, and may extend it with its own dialogs. Must be used on
/// the UI thread.
/// </summary>
public interface IDialogService
{
	/// <summary>Asks a yes/no question.</summary>
	/// <param name="defaultToNo">Makes "No" the default answer.</param>
	/// <param name="cancellationToken">Closes the question unanswered when cancelled.</param>
	/// <returns><see langword="true"/> only when the user answers yes.</returns>
	Task<bool> ShowYesNoConfirmation(string header, string message, bool defaultToNo = false,
		CancellationToken cancellationToken = default);
}
