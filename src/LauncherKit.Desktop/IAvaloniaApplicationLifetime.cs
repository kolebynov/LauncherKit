using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Desktop;

public interface IAvaloniaApplicationLifetime : IApplicationLifetime
{
	IMainView? MainWindow { get; }

	CancellationToken MainWindowOpened { get; }

	void ReloadViews();
}