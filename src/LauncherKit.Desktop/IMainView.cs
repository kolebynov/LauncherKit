namespace LauncherKit.Desktop;

/// <summary>
/// The consumer's main window registration. The implementation must derive from
/// <see cref="global::Avalonia.Controls.Window"/>; the consumer chooses its window base class.
/// Register it as a singleton. The application host assigns the current main model as its data context.
/// </summary>
public interface IMainView
{
	/// <summary>
	/// The page currently shown by the view's navigation (e.g. its frame's content), or <see langword="null"/>. When it is
	/// an <see cref="INavigationGuard"/>, it is asked before navigating away and before the window closes.
	/// </summary>
	object? CurrentContent { get; }

	/// <summary>
	/// Sets the guard the view passes every navigation to instead of running it: the guard runs it only when
	/// <see cref="CurrentContent"/> allows leaving and no other navigation is in progress. Without a guard the view
	/// navigates directly. The application host sets it once, before assigning the data context.
	/// </summary>
	void SetNavigationGuard(Func<Action, ValueTask> navigate);
}
