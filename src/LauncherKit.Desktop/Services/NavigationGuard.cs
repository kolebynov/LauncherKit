using LauncherKit.Core.Initialization;
using LauncherKit.Desktop.Extensions;

namespace LauncherKit.Desktop.Services;

/// <summary>
/// The guard set on <see cref="IMainView"/> when the main window is assigned to the lifetime, before its data context:
/// runs a navigation only when the current page allows leaving it, and skips navigations requested while one (its
/// question included) is in progress. UI thread only.
/// </summary>
internal sealed class NavigationGuard(IAvaloniaApplicationLifetime applicationLifetime) : IGlobalInitializer
{
	private bool _isNavigating;

	/// <summary>Asks <see cref="IMainView.CurrentContent"/> when it is an <see cref="INavigationGuard"/>; other content allows.</summary>
	public static ValueTask<bool> CanLeave(IMainView mainView) =>
		mainView.CurrentContent is INavigationGuard guard ? guard.CanNavigate() : ValueTask.FromResult(true);

	public void Initialize() => applicationLifetime.OnMainWindowAssigned(mainView => mainView.SetNavigationGuard(Navigate));

	public async ValueTask Navigate(Action navigate)
	{
		if (_isNavigating)
		{
			return;
		}

		_isNavigating = true;
		try
		{
			if (await CanLeave(applicationLifetime.RequiredMainWindow))
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
