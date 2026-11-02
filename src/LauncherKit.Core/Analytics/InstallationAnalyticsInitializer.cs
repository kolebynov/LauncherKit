using Microsoft.Extensions.Logging;
using LauncherKit.Core.Extensions;
using LauncherKit.Core.Initialization;

namespace LauncherKit.Core.Analytics;

internal sealed class InstallationAnalyticsInitializer : IGlobalInitializer
{
	private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

	private readonly IInstallationAnalyticsService _analyticsService;
	private readonly ILogger<InstallationAnalyticsInitializer> _logger;
	private readonly TimeProvider _timeProvider;

	public InstallationAnalyticsInitializer(
		IInstallationAnalyticsService analyticsService,
		ILogger<InstallationAnalyticsInitializer> logger,
		TimeProvider timeProvider)
	{
		_analyticsService = analyticsService;
		_logger = logger;
		_timeProvider = timeProvider;
	}

	public ValueTask Initialize()
	{
		ReportInstallation().Forget();
		return ValueTask.CompletedTask;
	}

	private async Task ReportInstallation()
	{
		using var cancellationTokenSource = new CancellationTokenSource(RequestTimeout, _timeProvider);
		try
		{
			await _analyticsService.ReportInstallation(cancellationTokenSource.Token);
		}
		catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
		{
			_logger.LogWarning("Timed out while reporting launcher installation analytics");
		}
		catch (Exception e)
		{
			_logger.LogWarning(e, "Failed to report launcher installation analytics");
		}
	}
}
