using LauncherKit.Infrastructure;
using Microsoft.Extensions.Logging;

namespace LauncherKit.LogsCollector.Core;

public sealed class SkseLogsSource(IFileSystemUtils fileSystemUtils) : ILogsSource
{
	public IEnumerable<LogFile> GetFiles(string? modOrganizerDirectory, ILogger logger) =>
		ExternalLogsFiles.GetFiles(Path.Combine(fileSystemUtils.GetSpecialFolderPath(Environment.SpecialFolder.MyDocuments),
			"My Games", "Skyrim Special Edition", "SKSE"), "skse", logger);
}
