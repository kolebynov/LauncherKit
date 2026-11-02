using Avalonia;

namespace LauncherKit.Desktop.Avalonia;

/// <summary>Entry point of a consumer launcher executable built on <see cref="LauncherKitApp"/>.</summary>
public static class LauncherKitProgram
{
	/// <summary>
	/// Runs the launcher with the classic desktop lifetime. Console output goes to <c>console.log</c> in the working
	/// directory, which the log collector includes; a failure that escapes the application is written there and rethrown.
	/// </summary>
	public static void Run<TApp>(string[] args) where TApp : LauncherKitApp, new()
	{
		AppContext.SetSwitch("System.Net.SocketsHttpHandler.Http3Support", false);

		StreamWriter? consoleWriter = null;
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
			BuildAvaloniaApp<TApp>().StartWithClassicDesktopLifetime(args);
		}
		catch (Exception e)
		{
			Console.WriteLine(e);
			throw;
		}
		finally
		{
			consoleWriter?.Dispose();
		}
	}

	/// <summary>The Avalonia configuration of the launcher; the consumer's <c>Program.BuildAvaloniaApp</c> returns it for the designer.</summary>
	public static AppBuilder BuildAvaloniaApp<TApp>() where TApp : LauncherKitApp, new()
		=> AppBuilder.Configure<TApp>()
			.UsePlatformDetect()
			.WithInterFont()
			.LogToTrace()
			.UseR3();
}
