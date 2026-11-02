using CommunityToolkit.Mvvm.ComponentModel;
using LauncherKit.Desktop.ViewModels;
using LauncherKit.Core.Feed.Modpacks;

namespace LauncherKit.Desktop.ViewModels;

public sealed partial class ModpackChangelogListViewModel : ViewModelBase
{
	[ObservableProperty]
	public partial IReadOnlyList<ModpackChangelogFeedItem> ModpackChangelogs { get; set; } = [];

	[ObservableProperty]
	public partial ModpackChangelogFeedItem? SelectedChangelog { get; set; }
}