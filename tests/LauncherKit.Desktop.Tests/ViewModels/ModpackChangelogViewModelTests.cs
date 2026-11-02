using NuGet.Versioning;
using LauncherKit.Desktop.ViewModels;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.Modpacks;

namespace LauncherKit.Desktop.Tests.ViewModels;

[TestFixture]
public sealed class ModpackChangelogViewModelTests
{
	[Test]
	public async Task Activate_UnorderedChangelogs_SortsNewestFirstAndSelectsNewest()
	{
		var older = new ModpackChangelogFeedItem { Version = NuGetVersion.Parse("1.0.0"), Path = new() { ["ru"] = "old.md" } };
		var newer = new ModpackChangelogFeedItem { Version = NuGetVersion.Parse("2.0.0"), Path = new() { ["ru"] = "new.md" } };
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IFeedProvider>()
			.Setup(x => x.GetModpackChangelogs(CancellationToken.None))
			.Returns(() => ValueTask.FromResult<IReadOnlyList<ModpackChangelogFeedItem>>([older, newer]));
		var sut = mocker.CreateInstance<ModpackChangelogViewModel>();

		using var activation = await sut.Activator.Activate(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(sut.ModpackChangelogListViewModel.ModpackChangelogs, Is.EqualTo(new[] { newer, older }));
			Assert.That(sut.ModpackChangelogListViewModel.SelectedChangelog, Is.SameAs(newer));
		});
	}

	[Test]
	public async Task Activate_NoChangelogs_LeavesSelectionEmpty()
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IFeedProvider>()
			.Setup(x => x.GetModpackChangelogs(CancellationToken.None))
			.Returns(() => ValueTask.FromResult<IReadOnlyList<ModpackChangelogFeedItem>>([]));
		var sut = mocker.CreateInstance<ModpackChangelogViewModel>();

		using var activation = await sut.Activator.Activate(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(sut.ModpackChangelogListViewModel.ModpackChangelogs, Is.Empty);
			Assert.That(sut.ModpackChangelogListViewModel.SelectedChangelog, Is.Null);
		});
	}
}
