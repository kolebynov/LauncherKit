using System.ComponentModel;
using Microsoft.Extensions.Options;
using NuGet.Versioning;
using R3;
using LauncherKit.Core.Extensions;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.Services;

/// <summary>
/// Reads the installed modpack version from <see cref="LauncherProductOptions.ModpackVersionFile"/>, a file holding
/// only the version string. Public so a consumer can decorate it.
/// </summary>
public sealed class ModpackVersionProvider : IModpackVersionProvider
{
	private string? _versionFilePath;

	public NuGetVersion? InstalledModpackVersion
	{
		get;
		private set
		{
			if (value != field)
			{
				field = value;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InstalledModpackVersion)));
			}
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public ModpackVersionProvider(IOptionsMonitor<AppSettings> appSettingsMonitor, IOptions<LauncherProductOptions> product)
	{
		appSettingsMonitor.ToObservable()
			.Subscribe(appSettings =>
			{
				_versionFilePath = !string.IsNullOrEmpty(appSettings.SkyrimDirectory)
					? new SkyrimRelativePath(product.Value.ModpackVersionFile).ToAbsolute(appSettings.SkyrimDirectory)
					: null;
				RefreshInstalledModpackVersion();
			});
	}

	public void RefreshInstalledModpackVersion()
	{
		InstalledModpackVersion = GetInstalledModpackVersion();

		NuGetVersion? GetInstalledModpackVersion() =>
			_versionFilePath != null && File.Exists(_versionFilePath)
				? NuGetVersion.Parse(File.ReadAllText(_versionFilePath))
				: null;
	}
}