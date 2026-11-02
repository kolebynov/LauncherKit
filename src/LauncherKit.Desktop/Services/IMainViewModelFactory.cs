using LauncherKit.Desktop.ViewModels;
using LauncherKit.Desktop.Services;

namespace LauncherKit.Desktop.Services;

public interface IMainViewModelFactory
{
	bool IsSetupRequired { get; }
	HomeViewModel? CreateHome();
	FirstSetupViewModel? CreateFirstSetup();
	SettingsViewModel? CreateSettings();
	DownloadsViewModel? CreateDownloads();
	ModpackChangelogViewModel? CreateModpackChangelog();
	GraphPacksViewModel? CreateGraphPacks();
	OptionalModsViewModel? CreateOptionalMods();
}
