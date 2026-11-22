using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Desktop.Extensions;

namespace LauncherKit.Desktop.Services;

internal sealed class ExternalBrowser : IExternalBrowser
{
	private readonly IAvaloniaApplicationLifetime _applicationLifetime;
	private readonly IUiDispatcher _uiDispatcher;

	public ExternalBrowser(IAvaloniaApplicationLifetime applicationLifetime, IUiDispatcher uiDispatcher)
	{
		_applicationLifetime = applicationLifetime;
		_uiDispatcher = uiDispatcher;
	}

	public async Task Open(Uri uri)
	{
		if (_applicationLifetime.MainWindow?.AsWindow() is not { } mainWindow)
		{
			return;
		}

		var opened = await _uiDispatcher.InvokeAsync(() => mainWindow.Launcher.LaunchUriAsync(uri));
		if (!opened)
		{
			throw new InvalidOperationException($"Failed to open {uri.GetLeftPart(UriPartial.Path)} in the browser");
		}
	}
}
