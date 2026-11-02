using NuGet.Versioning;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Initialization;

/// <summary>
/// Moves partial downloads left by launchers older than <see cref="FirstVersionWithFileParts"/> to the
/// in-progress path, because those launchers wrote them directly to the final path.
/// </summary>
internal sealed class LegacyPartialDownloadsInitializer : IConfiguredScopeInitializer
{
	private static readonly NuGetVersion FirstVersionWithFileParts = NuGetVersion.Parse("1.19.0-beta.0");

	private readonly IAppVersionProvider _appVersionProvider;
	private readonly IFileStorageService _fileStorageService;

	public LegacyPartialDownloadsInitializer(
		IAppVersionProvider appVersionProvider, IFileStorageService fileStorageService)
	{
		_appVersionProvider = appVersionProvider;
		_fileStorageService = fileStorageService;
	}

	public ValueTask Initialize()
	{
		var lastRunVersion = _appVersionProvider.LastRunVersion;
		if (lastRunVersion != null && lastRunVersion < FirstVersionWithFileParts)
		{
			_fileStorageService.MigrateLegacyPartialDownloads();
		}

		return ValueTask.CompletedTask;
	}
}
