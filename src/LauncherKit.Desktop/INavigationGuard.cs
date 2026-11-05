namespace LauncherKit.Desktop;

/// <summary>
/// Implemented by a page shown as <see cref="IMainView.CurrentContent"/> to be asked before the main view navigates away
/// from it or the main window closes (e.g. to save or discard unsaved changes). Runs on the UI thread.
/// </summary>
public interface INavigationGuard
{
	/// <summary>Returns whether the page may be left; <see langword="false"/> keeps it shown and the window open.</summary>
	ValueTask<bool> CanNavigate();
}
