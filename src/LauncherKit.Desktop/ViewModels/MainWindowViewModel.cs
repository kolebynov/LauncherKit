using LauncherKit.Desktop.Services;
using LauncherKit.Desktop.ViewModels;

namespace LauncherKit.Desktop.ViewModels;

/// <summary>Available desktop pages. Navigation and page composition belong to the consumer shell.</summary>
public sealed class MainWindowViewModel : ViewModelBase
{
	public bool IsSetupRequired { get; }
	public HomeViewModel? HomeViewModel { get; }
	public FirstSetupViewModel? FirstSetupViewModel { get; }
	public AppTaskQueueViewModel AppTaskQueueViewModel { get; }
	public DownloadsViewModel? DownloadsViewModel { get; }
	public SettingsViewModel? SettingsViewModel { get; }
	public ModpackChangelogViewModel? ModpackChangelogViewModel { get; }
	public GraphPacksViewModel? GraphPacksViewModel { get; }
	public OptionalModsViewModel? OptionalModsViewModel { get; }

	public MainWindowViewModel(AppTaskQueueViewModel appTaskQueueViewModel, IMainViewModelFactory factory)
	{
		AppTaskQueueViewModel = appTaskQueueViewModel;
		IsSetupRequired = factory.IsSetupRequired;
		HomeViewModel = factory.CreateHome();
		FirstSetupViewModel = factory.CreateFirstSetup();
		SettingsViewModel = factory.CreateSettings();
		DownloadsViewModel = factory.CreateDownloads();
		ModpackChangelogViewModel = factory.CreateModpackChangelog();
		GraphPacksViewModel = factory.CreateGraphPacks();
		OptionalModsViewModel = factory.CreateOptionalMods();
	}
}
