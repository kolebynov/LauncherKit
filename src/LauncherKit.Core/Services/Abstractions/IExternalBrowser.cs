namespace LauncherKit.Core.Services.Abstractions;

/// <summary>
/// Opens web pages in the user's default browser.
/// </summary>
public interface IExternalBrowser
{
	/// <exception cref="InvalidOperationException">The browser could not be started.</exception>
	Task Open(Uri uri);
}
