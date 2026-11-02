namespace LauncherKit.Core.Services.Abstractions;

public interface IAppLogsCollector
{
	Task CollectLogs(CancellationToken cancellationToken);
}
