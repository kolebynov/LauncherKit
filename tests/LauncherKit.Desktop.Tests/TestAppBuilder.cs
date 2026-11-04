using Avalonia;
using Avalonia.Headless;

namespace LauncherKit.Desktop.Tests;

public static class TestAppBuilder
{
	public static AppBuilder BuildAvaloniaApp() =>
		AppBuilder.Configure<Application>()
			.UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
