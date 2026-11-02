using Microsoft.Extensions.Logging;

namespace LauncherKit.LogsCollector.Core;

public sealed class ModOrganizerLogsSource : ILogsSource
{
	public IEnumerable<LogFile> GetFiles(string? modOrganizerDirectory, ILogger logger) =>
		string.IsNullOrEmpty(modOrganizerDirectory)
			? []
			: ExternalLogsFiles.GetFiles(Path.Combine(modOrganizerDirectory, "logs"), "mod-organizer", logger);
}
