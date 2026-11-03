using CommunityToolkit.Mvvm.ComponentModel;
using LauncherKit.Core.Feed.Modpacks;

namespace LauncherKit.Desktop.ViewModels;

public partial class ModpackChangelogListViewModel : ViewModelBase
{
	[ObservableProperty]
	public partial IReadOnlyList<ModpackChangelogFeedItem> ModpackChangelogs { get; set; } = [];

	[ObservableProperty]
	public partial ModpackChangelogFeedItem? SelectedChangelog { get; set; }
}