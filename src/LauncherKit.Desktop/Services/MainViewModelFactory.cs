using Microsoft.Extensions.DependencyInjection;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Services;
using LauncherKit.Desktop.ViewModels;
using Microsoft.Extensions.Options;
using LauncherKit.Infrastructure;

namespace LauncherKit.Desktop.Services;

public sealed class MainViewModelFactory : IMainViewModelFactory
{
	private readonly IServiceProvider _serviceProvider;

	public MainViewModelFactory(IServiceProvider serviceProvider, IOptionsSnapshot<AppSettings> appSettings)
	{
		_serviceProvider = serviceProvider;
		IsSetupRequired = string.IsNullOrEmpty(appSettings.Value.SkyrimDirectory);
	}

	public bool IsSetupRequired { get; }

	public HomeViewModel? CreateHome() => IsSetupRequired ? null : _serviceProvider.GetRequiredService<HomeViewModel>();
	public FirstSetupViewModel? CreateFirstSetup() => IsSetupRequired ? _serviceProvider.GetRequiredService<FirstSetupViewModel>() : null;
	public SettingsViewModel? CreateSettings() => _serviceProvider.GetService<SettingsViewModel>();
	public DownloadsViewModel? CreateDownloads() => IsSetupRequired ? null : _serviceProvider.GetService<DownloadsViewModel>();
	public ModpackChangelogViewModel? CreateModpackChangelog() => IsSetupRequired ? null : _serviceProvider.GetService<ModpackChangelogViewModel>();
	public GraphPacksViewModel? CreateGraphPacks() => IsSetupRequired ? null : _serviceProvider.GetService<GraphPacksViewModel>();
	public OptionalModsViewModel? CreateOptionalMods() => IsSetupRequired ? null : _serviceProvider.GetService<OptionalModsViewModel>();
}
