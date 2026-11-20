using Avalonia;
using LauncherKit.Desktop;

namespace SampleLauncher.App;

internal sealed class Program
{
	[STAThread]
	public static void Main(string[] args) => LauncherKitProgram.Run<App>(args);

	// Used by the Avalonia designer.
	public static AppBuilder BuildAvaloniaApp() => LauncherKitProgram.BuildAvaloniaApp<App>();
}
