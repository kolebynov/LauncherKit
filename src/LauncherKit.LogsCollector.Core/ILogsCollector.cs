namespace LauncherKit.LogsCollector.Core;

public interface ILogsCollector
{
	Task<Exception?> CollectLogs(string outputFileDirectory, string? modOrganizerDirectory, CancellationToken cancellationToken);
}
