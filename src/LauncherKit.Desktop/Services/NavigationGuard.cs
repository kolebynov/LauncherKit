namespace LauncherKit.Desktop.Services;

/// <summary>
/// The guard the application host sets on <see cref="IMainView"/>: runs a navigation only when the current page allows
/// leaving it, and skips navigations requested while one (its question included) is in progress. UI thread only.
/// </summary>
internal sealed class NavigationGuard(IMainView mainView)
{
	private bool _isNavigating;

	/// <summary>Asks <see cref="IMainView.CurrentContent"/> when it is an <see cref="INavigationGuard"/>; other content allows.</summary>
	public static ValueTask<bool> CanLeave(IMainView mainView) =>
		mainView.CurrentContent is INavigationGuard guard ? guard.CanNavigate() : ValueTask.FromResult(true);

	public async ValueTask Navigate(Action navigate)
	{
		if (_isNavigating)
		{
			return;
		}

		_isNavigating = true;
		try
		{
			if (await CanLeave(mainView))
			{
				navigate();
			}
		}
		finally
		{
			_isNavigating = false;
		}
	}
}
