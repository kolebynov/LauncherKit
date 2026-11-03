using LauncherKit.Infrastructure;
using Microsoft.Extensions.Logging;

namespace LauncherKit.LogsCollector.Core;

public sealed class LauncherLogsSource(ILaunchersRootDirectoryManager launchersRootDirectoryManager) : ILogsSource
{
	public IEnumerable<LogFile> GetFiles(string? modOrganizerDirectory, ILogger logger)
	{
		string root;
		try
		{
			root = launchersRootDirectoryManager.Directory;
		}
		catch (InvalidOperationException e)
		{
			// No installed launcher (e.g. one installed before the installer recorded its root): other sources still run.
			logger.LogWarning(e, "Launchers root directory is unknown");
			return [];
		}

		return GetLauncherFiles(root, logger);
	}

	private IEnumerable<LogFile> GetLauncherFiles(string root, ILogger logger)
	{
		foreach (var file in Directory.EnumerateFiles(root))
		{
			yield return new LogFile(file, Path.GetRelativePath(root, file));
		}

		var directory = launchersRootDirectoryManager.TryGetInstalledLauncherDirectory();
		logger.LogInformation("Detected launcher directory {LauncherDirectory}", directory);
		if (directory == null)
		{
			yield break;
		}

		if (!Directory.Exists(directory))
		{
			logger.LogWarning("Launcher directory {LauncherDirectory} does not exist", directory);
			yield break;
		}

		foreach (var file in Directory.EnumerateFiles(directory, "appsettings.*"))
		{
			yield return new LogFile(file, Path.GetRelativePath(root, file));
		}

		var console = Path.Combine(directory, "console.log");
		yield return new LogFile(console, Path.GetRelativePath(root, console));
		var logs = Path.Combine(directory, "logs");
		if (Directory.Exists(logs))
		{
			foreach (var file in Directory.EnumerateFiles(logs))
			{
				yield return new LogFile(file, Path.GetRelativePath(root, file));
			}
		}
	}
}
