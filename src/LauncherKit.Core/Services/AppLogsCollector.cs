using Microsoft.Extensions.Options;
using LauncherKit.Infrastructure;
using LauncherKit.Core.Extensions;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.LogsCollector.Core;

namespace LauncherKit.Core.Services;

public sealed class AppLogsCollector : IAppLogsCollector
{
	private readonly ILogsCollector _logsCollector;
	private readonly IProcessRunner _processRunner;
	private readonly ITempStorageService _tempStorageService;
	private readonly IOptionsMonitor<AppSettings> _appSettings;
	private readonly LauncherProductOptions _product;

	public AppLogsCollector(ILogsCollector logsCollector, IProcessRunner processRunner,
		ITempStorageService tempStorageService, IOptionsMonitor<AppSettings> appSettings, IOptions<LauncherProductOptions> product)
	{
		_logsCollector = logsCollector;
		_processRunner = processRunner;
		_tempStorageService = tempStorageService;
		_appSettings = appSettings;
		_product = product.Value;
	}

	public async Task CollectLogs(CancellationToken cancellationToken)
	{
		var tempDirectory = _tempStorageService.CreateTempDirectory();
		try
		{
			await _logsCollector.CollectLogs(tempDirectory.Path, _appSettings.CurrentValue.GetModOrganizerDirectory(_product), cancellationToken);
			_processRunner.OpenDirectory(tempDirectory.Path);
		}
		catch
		{
			tempDirectory.Dispose();
			throw;
		}
	}
}