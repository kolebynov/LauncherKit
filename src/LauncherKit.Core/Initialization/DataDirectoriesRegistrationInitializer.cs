using System.Security;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LauncherKit.Core.Initialization;

/// <summary>
/// Publishes the launcher's folders in the user-chosen data directory (downloads and temporary files) under the
/// consumer's <see cref="LauncherProductOptions.RegistryKey"/>, where the consumer's MSI reads them for its "remove all
/// data" uninstall option: Windows Installer cannot read <c>appsettings.override.json</c>. Written on start and whenever
/// the settings change. The data directory itself, which the MSI removes only when it is empty afterwards, is not
/// published when it is a drive root.
/// </summary>
internal sealed class DataDirectoriesRegistrationInitializer : IGlobalInitializer, IDisposable
{
	private readonly IOptionsMonitor<AppSettings> _appSettingsMonitor;
	private readonly IRegistryManager _registryManager;
	private readonly ILogger<DataDirectoriesRegistrationInitializer> _logger;
	private readonly string _registryKey;
	private IDisposable? _subscription;

	public DataDirectoriesRegistrationInitializer(IOptionsMonitor<AppSettings> appSettingsMonitor,
		IRegistryManager registryManager, IOptions<LauncherProductOptions> product,
		ILogger<DataDirectoriesRegistrationInitializer> logger)
	{
		_appSettingsMonitor = appSettingsMonitor;
		_registryManager = registryManager;
		_registryKey = product.Value.RegistryKey;
		_logger = logger;
	}

	public ValueTask Initialize()
	{
		Register(_appSettingsMonitor.CurrentValue);
		_subscription = _appSettingsMonitor.OnChange((settings, _) => Register(settings));
		return ValueTask.CompletedTask;
	}

	public void Dispose() => _subscription?.Dispose();

	private void Register(AppSettings settings)
	{
		if (string.IsNullOrEmpty(settings.AppDataDirectory))
		{
			return;
		}

		var appDataDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(settings.AppDataDirectory));
		try
		{
			_registryManager.SetValue(_registryKey, "DownloadsDirectory", Path.GetFullPath(settings.GetDownloadsDirectory()!));
			_registryManager.SetValue(_registryKey, "TempDataDirectory", Path.GetFullPath(settings.GetTempDataDirectory()!));
			if (!string.Equals(appDataDirectory, Path.TrimEndingDirectorySeparator(Path.GetPathRoot(appDataDirectory)!),
				StringComparison.OrdinalIgnoreCase))
			{
				_registryManager.SetValue(_registryKey, "AppDataDirectory", appDataDirectory);
			}
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
		{
			_logger.LogWarning(e, "Failed to register the data directories in the registry");
		}
	}
}
