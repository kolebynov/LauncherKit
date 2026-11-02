using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.Options;
using NuGet.Versioning;
using R3;
using LauncherKit.Core.Extensions;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.Services;

internal sealed class ModpackVersionProvider : IModpackVersionProvider
{
	private string? _translationsFilePath;

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
				_translationsFilePath = !string.IsNullOrEmpty(appSettings.SkyrimDirectory)
					? new SkyrimRelativePath(product.Value.ModpackVersionFile).ToAbsolute(appSettings.SkyrimDirectory)
					: null;
				RefreshInstalledModpackVersion();
			});
	}

	public void RefreshInstalledModpackVersion()
	{
		InstalledModpackVersion = GetInstalledModpackVersion();

		NuGetVersion? GetInstalledModpackVersion()
		{
			if (_translationsFilePath == null || !File.Exists(_translationsFilePath))
			{
				return null;
			}

			const string VersionKey = "$NowVersionMZV";
			var versionString = File.ReadAllLines(_translationsFilePath, Encoding.Unicode)
				.Where(x => x.StartsWith(VersionKey, StringComparison.Ordinal))
				.Select(x => x[(VersionKey.Length + 1)..].Trim())
				.FirstOrDefault();
			return versionString != null ? NuGetVersion.Parse(versionString) : null;
		}
	}
}