namespace LauncherKit.Desktop.Extensions;

public static class ApplicationLifetimeExtensions
{
	extension (IAvaloniaApplicationLifetime applicationLifetime)
	{
		public IMainView RequiredMainWindow =>
			applicationLifetime.MainWindow ?? throw new InvalidOperationException("Main window is not initialized.");
	}
}