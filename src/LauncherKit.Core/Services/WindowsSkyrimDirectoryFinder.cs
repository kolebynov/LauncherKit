using System.Runtime.Versioning;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Utils;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.Services;

[SupportedOSPlatform("Windows")]
internal sealed class WindowsSkyrimDirectoryFinder : ISkyrimDirectoryFinder
{
	private static readonly string[] ScanRegistries = [
		@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
		@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
	];

	private readonly IRegistryManager _registryManager;
	private readonly ISkyrimInstallationChecker _skyrimInstallationChecker;

	public WindowsSkyrimDirectoryFinder(IRegistryManager registryManager,
		ISkyrimInstallationChecker skyrimInstallationChecker)
	{
		_registryManager = registryManager;
		_skyrimInstallationChecker = skyrimInstallationChecker;
	}

	public string? FindSkyrimDirectory()
	{
		foreach (var registryKey in ScanRegistries)
		{
			foreach (var subkeyName in _registryManager.GetSubKeyNames(registryKey))
			{
				var subkeyPath = $@"{registryKey}\{subkeyName}";
				if (_registryManager.GetValue(subkeyPath, "DisplayName") is string displayName &&
				    displayName.Contains("skyrim", StringComparison.OrdinalIgnoreCase) &&
				    _registryManager.GetValue(subkeyPath, "InstallLocation") is string installLocation &&
				    _skyrimInstallationChecker.CheckSkyrimDirectory(installLocation) is SkyrimDirectoryCheckResult.SkyrimDirectory)
				{
					return installLocation;
				}
			}
		}

		return null;
	}
}
