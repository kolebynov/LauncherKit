using LauncherKit.Desktop.Extensions;
using LauncherKit.Core.Feed;

namespace LauncherKit.Desktop.ViewModels;

public class ModpackChangelogViewModel : BaseActivatableViewModel
{
	public ModpackChangelogListViewModel ModpackChangelogListViewModel { get; } = new();

	public ModpackChangelogViewModel(IFeedProvider feedProvider)
	{
		this.WhenActivated(async (_, ct) =>
		{
			ModpackChangelogListViewModel.ModpackChangelogs = (await feedProvider.GetModpackChangelogs(ct)).OrderByDescending(x => x.Version).ToList();
			ModpackChangelogListViewModel.SelectedChangelog = ModpackChangelogListViewModel.ModpackChangelogs.Count == 0
				? null
				: ModpackChangelogListViewModel.ModpackChangelogs[0];
		});
	}

}
