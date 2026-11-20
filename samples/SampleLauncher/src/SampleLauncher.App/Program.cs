using Avalonia;
using LauncherKit.Desktop;

namespace SampleLauncher.App;

internal sealed class Program
{
	[STAThread]
	public static int Main(string[] args) => LauncherKitProgram.Run<App>(args, ProductIdentity.CreateOptions(),
		(launch, sender) => sender.Send(new SecondLaunchMessage([.. launch.Args], launch.WorkingDirectory),
			SampleJsonContext.Default.SecondLaunchMessage));

	// Used by the Avalonia designer.
	public static AppBuilder BuildAvaloniaApp() => LauncherKitProgram.BuildAvaloniaApp<App>(ProductIdentity.CreateOptions());
}
