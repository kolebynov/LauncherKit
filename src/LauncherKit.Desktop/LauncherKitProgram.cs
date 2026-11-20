using Avalonia;
using LauncherKit.Desktop.SingleInstance;
using LauncherKit.Infrastructure;

namespace LauncherKit.Desktop;

/// <summary>Entry point of a consumer launcher executable built on <see cref="LauncherKitApp"/>.</summary>
public static class LauncherKitProgram
{
	/// <summary>
	/// Runs the launcher with the classic desktop lifetime unless a launcher already runs for the same product
	/// (<see cref="LauncherProductOptions.RegistryKey"/>), Windows user and session. Then this launch asks it to activate its
	/// main window, runs <paramref name="onSecondLaunch"/> with this launch's arguments and working directory to send any
	/// messages to the handlers registered with
	/// <see cref="LauncherKitDesktopServiceCollectionExtensions.AddLauncherMessageHandler{TMessage, THandler}"/>, and exits.
	/// Delivery is one-way and best effort. The first launch ignores <paramref name="onSecondLaunch"/>: the consumer handles
	/// its own arguments at startup. The owner's console output goes to <c>console.log</c> in the working directory, which
	/// the log collector includes; a failure that escapes the application is written there and rethrown.
	/// </summary>
	/// <returns>The application's exit code; always 0 for a second launch, whether or not the owner was reached.</returns>
	public static int Run<TApp>(string[] args, LauncherProductOptions product,
		Action<LaunchContext, ILauncherMessageSender>? onSecondLaunch = null) where TApp : LauncherKitApp, new()
	{
		product.Validate();
		AppContext.SetSwitch("System.Net.SocketsHttpHandler.Http3Support", false);

		var names = SingleInstanceNames.ForCurrentUser(product.RegistryKey);
		var election = SingleInstanceElection.Run(names, onSecondLaunch is null
			? null
			: sender => onSecondLaunch(new LaunchContext(args, Directory.GetCurrentDirectory()), sender));
		return election switch
		{
			SingleInstanceElection.Result.Secondary => 0,
			SingleInstanceElection.Result.Owner(var mutex) => RunOwner<TApp>(args, product, names, mutex),
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

	private static int RunOwner<TApp>(string[] args, LauncherProductOptions product, SingleInstanceNames names,
		Mutex ownership) where TApp : LauncherKitApp, new()
	{
		using var launchRequests = new LaunchRequestQueue();
		StreamWriter? consoleWriter = null;
		try
		{
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
