namespace LauncherKit.Core.Analytics;

internal interface IInstallationAnalyticsService
{
	Task ReportInstallation(CancellationToken cancellationToken);
}
