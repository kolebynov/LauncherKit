using Microsoft.Extensions.Logging;

namespace LauncherKit.LogsCollector.Core;

internal static class ExternalLogsFiles
{
	public static IEnumerable<LogFile> GetFiles(string directory, string entryPrefix, ILogger logger)
	{
		if (!Directory.Exists(directory))
		{
			logger.LogWarning("Logs directory {Directory} does not exist", directory);
			yield break;
		}

		foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*.log", SearchOption.TopDirectoryOnly)
			.OrderByDescending(x => x.LastWriteTimeUtc).Take(10))
		{
			yield return new LogFile(file.FullName, Path.Combine(entryPrefix, file.Name));
		}
	}
}
