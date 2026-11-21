using Avalonia;
using LauncherKit.Desktop.SingleInstance;
using LauncherKit.Infrastructure;

namespace LauncherKit.Desktop;

/// <summary>Entry point of a consumer launcher executable built on <see cref="LauncherKitApp"/>.</summary>
public static class LauncherKitProgram
{
	/// <summary>
	/// Runs the launcher with the classic desktop lifetime unless a launcher already runs for the same product
	/// (<see cref="LauncherProductOptions.RegistryKey"/>), Windows user and session. Every launch's arguments and working
	/// directory reach the <see cref="ILaunchHandler"/>s registered with
	/// <see cref="LauncherKitDesktopServiceCollectionExtensions.AddLaunchHandler{THandler}"/> in the running launcher as a
	/// <see cref="LaunchContext"/>: this launch's own once the main window has opened, and each later launch's, which
	/// forwards it one-way and best effort, lets the running launcher take the foreground and exits. The kit's first
	/// registered handler (added by <see cref="LauncherKitDesktopServiceCollectionExtensions.AddLauncherKitDesktop"/>)
	/// brings the main window to the front for every later launch, as far as Windows allows.
	/// The owner's console output goes to <c>console.log</c> in the working directory, which the log
	/// collector includes; a failure that escapes the application is written there and rethrown.
	/// </summary>
	/// <returns>The application's exit code; always 0 for a second launch, whether or not the owner was reached.</returns>
	public static int Run<TApp>(string[] args, LauncherProductOptions product) where TApp : LauncherKitApp, new()
	{
		product.Validate();
		AppContext.SetSwitch("System.Net.SocketsHttpHandler.Http3Support", false);

		var workingDirectory = Directory.GetCurrentDirectory();
		var names = SingleInstanceNames.ForCurrentUser(product.RegistryKey);
		return SingleInstanceElection.Run(names, args, workingDirectory) switch
		{
			SingleInstanceElection.Result.Secondary => 0,
			SingleInstanceElection.Result.Owner(var mutex) => RunOwner<TApp>(args, workingDirectory, product, names, mutex),
		};
	}

	/// <summary>
	/// The Avalonia configuration of the launcher; the consumer's <c>Program.BuildAvaloniaApp</c> returns it for the designer,
	/// which neither takes the launcher's ownership nor receives relaunches.
	/// </summary>
	public static AppBuilder BuildAvaloniaApp<TApp>(LauncherProductOptions product) where TApp : LauncherKitApp, new()
		=> BuildAvaloniaApp<TApp>(product, null);

	private static AppBuilder BuildAvaloniaApp<TApp>(LauncherProductOptions product, LaunchRequestQueue? launchRequests)
		where TApp : LauncherKitApp, new()
		=> AppBuilder.Configure(() => new TApp { Product = product, OwnerLaunchRequests = launchRequests })
			.UsePlatformDetect()
			.WithInterFont()
			.LogToTrace()
			.UseR3();

	private static int RunOwner<TApp>(string[] args, string workingDirectory, LauncherProductOptions product,
		SingleInstanceNames names, Mutex ownership) where TApp : LauncherKitApp, new()
	{
		using var launchRequests = new LaunchRequestQueue();
		StreamWriter? consoleWriter = null;
		try
		{
			// Admitted before the listener exists, so this launch is handled before any relaunch.
			launchRequests.TryEnqueue(new LaunchContext(args, workingDirectory, true));
			using var listener = new SingleInstanceListener(names.Pipe, launchRequests);
			try
			{
				consoleWriter = File.CreateText("console.log");
				consoleWriter.AutoFlush = true;
				Console.SetOut(consoleWriter);
				Console.SetError(consoleWriter);
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException)
			{
				// Without a writable console.log the launcher still runs; only console output is lost.
			}

			try
			{
				return BuildAvaloniaApp<TApp>(product, launchRequests).StartWithClassicDesktopLifetime(args);
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				throw;
			}
		}
		finally
		{
			launchRequests.Complete();
			consoleWriter?.Dispose();
			ownership.ReleaseMutex();
			ownership.Dispose();
		}
	}
}
