using Avalonia.Controls;
using LauncherKit.Desktop.Avalonia.Services;
using Avalonia.Threading;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Desktop.Avalonia.Services;

internal sealed class ExternalBrowser : IExternalBrowser
{
	private readonly Func<TopLevel> _topLevelProvider;

	public ExternalBrowser(Func<TopLevel> topLevelProvider)
	{
		_topLevelProvider = topLevelProvider;
	}

	public async Task Open(Uri uri)
	{
		var opened = await Dispatcher.UIThread.InvokeAsync(() => _topLevelProvider().Launcher.LaunchUriAsync(uri));
		if (!opened)
		{
			throw new InvalidOperationException($"Failed to open {uri.GetLeftPart(UriPartial.Path)} in the browser");
		}
	}
}
