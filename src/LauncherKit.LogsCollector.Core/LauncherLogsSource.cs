using LauncherKit.Infrastructure;
using Microsoft.Extensions.Logging;

namespace LauncherKit.LogsCollector.Core;

public sealed class LauncherLogsSource(ILaunchersRootDirectoryManager launchersRootDirectoryManager) : ILogsSource
{
	public IEnumerable<LogFile> GetFiles(string? modOrganizerDirectory, ILogger logger)
	{
		var root = launchersRootDirectoryManager.Directory;
		foreach (var file in Directory.EnumerateFiles(root))
		{
			yield return new LogFile(file, Path.GetRelativePath(root, file));
		}

		var version = launchersRootDirectoryManager.TryGetInstalledVersion();
		logger.LogInformation("Detected launcher version {LauncherVersion}", version);
		if (version == null)
		{
			yield break;
		}

		var directory = Path.Combine(root, version.ToString());
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
