using System.ComponentModel;
using LauncherKit.Desktop.Services;
using LauncherKit.Desktop.ViewModels;
using NuGet.Versioning;
using R3;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.Modpacks;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Update;
using LauncherKit.TestSupport;

namespace LauncherKit.Desktop.Tests.ViewModels;

[TestFixture]
public sealed class HomeViewModelTests
{
	[Test]
	public async Task LauncherChangelog_IsPendingUntilConsumerMarksItShown()
	{
		using var events = new Subject<AppTaskQueue.Event>();
		using var beta = new Subject<Unit>();
		var mocker = CreateMocker(events, beta);
		var storage = mocker.GetMock<IGlobalKeyValueStorage>();
		storage.Setup(x => x.HasKey("AppChangelogWasShown_1.2.0")).Returns(false);
		storage.Setup(x => x.Set("AppChangelogWasShown_1.2.0", "1"));
		using var sut = mocker.CreateInstance<HomeViewModel>();
		using var activation = await sut.Activator.Activate(CancellationToken.None);
		Assert.That(sut.LauncherChangelogToShow?.Version, Is.EqualTo(NuGetVersion.Parse("1.2.0")));
		storage.Verify(x => x.Set(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
		sut.MarkLauncherChangelogShown();
		Assert.That(sut.LauncherChangelogToShow, Is.Null);
		storage.Verify(x => x.Set("AppChangelogWasShown_1.2.0", "1"), Times.Once);
	}

	[Test]
	public async Task LauncherChangelog_AlreadyShown_IsNotPending()
	{
		using var events = new Subject<AppTaskQueue.Event>();
		using var beta = new Subject<Unit>();
		var mocker = CreateMocker(events, beta);
		mocker.GetMock<IGlobalKeyValueStorage>().Setup(x => x.HasKey("AppChangelogWasShown_1.2.0")).Returns(true);
		using var sut = mocker.CreateInstance<HomeViewModel>();
		using var activation = await sut.Activator.Activate(CancellationToken.None);
		Assert.That(sut.LauncherChangelogToShow, Is.Null);
	}

	[TestCase(0)]
	[TestCase(3)]
	public async Task DeleteOldSaves_ForwardsKeepCountAndReportsUnmodifiedOutcomeAfterCommandCompletes(int keep)
	{
		using var events = new Subject<AppTaskQueue.Event>();
		using var beta = new Subject<Unit>();
		var mocker = CreateMocker(events, beta);
		var task = new AppTask("DeleteOldSaves", new AppTaskDescription.DeleteOldSaves(), _ => [], [], null);
		Action<DeletedSavesInfo>? report = null;
		mocker.GetMock<IAppTaskProvider>().Setup(x => x.CreateDeleteOldSavesTask(keep, It.IsAny<Action<DeletedSavesInfo>>()))
			.Callback<int, Action<DeletedSavesInfo>>((_, callback) => report = callback).Returns(task);
		mocker.GetMock<IAppTaskQueue>().Setup(x => x.EnqueueAndWaitForCompletion(task, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		using var sut = mocker.CreateInstance<HomeViewModel>();
		var reported = new List<DeletedSavesInfo>();
		using var subscription = sut.OldSavesDeleted.Subscribe(reported.Add);
		await sut.DeleteOldSavesCommand.ExecuteAsync(keep);
		var error = new IOException("locked");
		var outcome = new DeletedSavesInfo(2, 512, error);
		report!(outcome);
		Assert.That(reported, Is.EqualTo(new[] { outcome }));
		Assert.That(reported[0].Error, Is.SameAs(error));
	}

	[Test]
	public async Task UninstallModpack_ActsDirectlyAndRefreshesVersionEvenOnFailure()
	{
		using var events = new Subject<AppTaskQueue.Event>();
		using var beta = new Subject<Unit>();
		var mocker = CreateMocker(events, beta);
		var task = new AppTask("Uninstall", new AppTaskDescription.Custom("Uninstall"), _ => [], [], null);
		mocker.GetMock<IAppTaskProvider>().Setup(x => x.CreateUninstallModpackTask()).Returns(task);
		mocker.GetMock<IAppTaskQueue>().Setup(x => x.EnqueueAndWaitForCompletion(task, It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("locked"));
		mocker.GetMock<IModpackVersionProvider>().Setup(x => x.RefreshInstalledModpackVersion());
		using var sut = mocker.CreateInstance<HomeViewModel>();
		await Assert.ThrowsAsync<IOException>(() => sut.UninstallModpackCommand.ExecuteAsync(null));
		mocker.GetMock<IModpackVersionProvider>().Verify(x => x.RefreshInstalledModpackVersion(), Times.Once);
	}

	[TestCase("3.1.0", "3.2.0", new[] { "3.2.0", "3.1.1" })]
	[TestCase(null, "3.2.0", new[] { "3.2.0", "3.1.1", "3.1.0", "3.0.0" })]
	[TestCase("3.2.0", "3.1.0", new string[0])]
	public async Task ModpackChangelog_InstallationChange_UsesCorrectVersionRange(string? previous, string current, string[] expected)
	{
		using var events = new Subject<AppTaskQueue.Event>();
		using var beta = new Subject<Unit>();
		var mocker = CreateMocker(events, beta);
		NuGetVersion? installed = previous == null ? null : NuGetVersion.Parse(previous);
		var provider = mocker.GetMock<IModpackVersionProvider>();
		provider.SetupGet(x => x.InstalledModpackVersion).Returns(() => installed);
		mocker.GetMock<IGlobalKeyValueStorage>().Setup(x => x.HasKey(It.IsAny<string>())).Returns(true);
		mocker.GetMock<IFeedProvider>().Setup(x => x.GetModpackChangelogs(It.IsAny<CancellationToken>())).ReturnsAsync(
			new[] { "2.9.0", "3.0.0", "3.1.0", "3.1.1", "3.2.0", "3.3.0" }.Select(v => new ModpackChangelogFeedItem
			{
				Version = NuGetVersion.Parse(v), Path = new() { ["en"] = v + ".md" },
			}).ToArray());
		using var sut = mocker.CreateInstance<HomeViewModel>();
		using var activation = await sut.Activator.Activate(CancellationToken.None);
		installed = NuGetVersion.Parse(current);
		provider.Raise(x => x.PropertyChanged += null, new PropertyChangedEventArgs(nameof(IModpackVersionProvider.InstalledModpackVersion)));
		if (expected.Length > 0) await TaskAssert.Eventually(() => sut.PendingModpackChangelog.Count == expected.Length, "pending changelog");
		Assert.That(sut.PendingModpackChangelog.Select(x => x.Version.ToString()), Is.EqualTo(expected));
		sut.MarkModpackChangelogShown();
		Assert.That(sut.PendingModpackChangelog, Is.Empty);
	}

	[Test]
	public async Task MissingInstallInfoWithoutInstalledVersion_ExposesTypedUnavailableReason()
	{
		using var events = new Subject<AppTaskQueue.Event>();
		using var beta = new Subject<Unit>();
		var mocker = CreateMocker(events, beta);
		mocker.GetMock<IModpackVersionProvider>().SetupGet(x => x.InstalledModpackVersion).Returns((NuGetVersion?)null);
		mocker.GetMock<IGlobalKeyValueStorage>().Setup(x => x.HasKey(It.IsAny<string>())).Returns(true);
		using var sut = mocker.CreateInstance<HomeViewModel>();
		using var activation = await sut.Activator.Activate(CancellationToken.None);
		Assert.That(sut.InstalledModpackVersion, Is.Null);
		Assert.That(sut.ModpackInstallUnavailableReason, Is.EqualTo(ModpackInstallUnavailableReason.NoAvailableVersions));
	}

	[Test]
	public async Task UpdateAndPatchCommands_QueueTheAvailableTaskDirectly()
	{
		using var events = new Subject<AppTaskQueue.Event>();
		using var beta = new Subject<Unit>();
		var mocker = CreateMocker(events, beta);
		var task = new AppTask("Patch", new AppTaskDescription.Custom("Patch"), _ => [], [], null);
		var info = new ModpackUpdateInfo { UpdateAppTask = task, UpdateType = new UpdateType.PatchUpdate(true), Version = NuGetVersion.Parse("3.2.1") };
		mocker.GetMock<IUpdateProvider>().Setup(x => x.GetModpackUpdateInfo(It.IsAny<CancellationToken>())).ReturnsAsync(info);
		mocker.GetMock<IAppTaskQueue>().Setup(x => x.EnqueueAndWaitForCompletion(task, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		mocker.GetMock<IModpackVersionProvider>().Setup(x => x.RefreshInstalledModpackVersion());
		mocker.GetMock<IGlobalKeyValueStorage>().Setup(x => x.HasKey(It.IsAny<string>())).Returns(true);
		using var sut = mocker.CreateInstance<HomeViewModel>();
		using var activation = await sut.Activator.Activate(CancellationToken.None);
		await sut.InstallModpackPatchCommand.ExecuteAsync(null);
		await sut.UpdateModpackCommand.ExecuteAsync(null);
		mocker.GetMock<IAppTaskQueue>().Verify(x => x.EnqueueAndWaitForCompletion(task, It.IsAny<CancellationToken>()), Times.Exactly(2));
	}

	[Test]
	public async Task InstallVersionCommand_ForwardsSelectedFeedItemAndQueuesItsTask()
	{
		using var events = new Subject<AppTaskQueue.Event>();
		using var beta = new Subject<Unit>();
		var mocker = CreateMocker(events, beta);
		var version = NuGetVersion.Parse("3.0.0");
		var feedItem = new ModpackFeedItem { InitialVersion = version, Version = version, DownloadInfo = new DownloadInfo { S3Path = "modpack.7z" } };
		var selected = new AvailableModpackVersion(feedItem, version, false);
		var task = new AppTask("Install", new AppTaskDescription.Custom("Install"), _ => [], [], null);
		var info = new ModpackUpdateInfo { UpdateAppTask = task, UpdateType = new UpdateType.InitialInstall(), Version = version };
		mocker.GetMock<IUpdateProvider>().Setup(x => x.GetModpackInstallInfo(feedItem, It.IsAny<CancellationToken>())).ReturnsAsync(info);
		mocker.GetMock<IAppTaskQueue>().Setup(x => x.EnqueueAndWaitForCompletion(task, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		mocker.GetMock<IModpackVersionProvider>().Setup(x => x.RefreshInstalledModpackVersion());
		using var sut = mocker.CreateInstance<HomeViewModel>();
		await sut.InstallModpackVersionCommand.ExecuteAsync(selected);
		mocker.GetMock<IAppTaskQueue>().Verify(x => x.EnqueueAndWaitForCompletion(task, It.IsAny<CancellationToken>()), Times.Once);
	}


	private static AutoMocker CreateMocker(Subject<AppTaskQueue.Event> events, Subject<Unit> beta)
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<IUiDispatcher>(new HomeCardsDispatcher());
		mocker.GetMock<IAppTaskQueue>().SetupGet(x => x.Events).Returns(events);
		var provider = mocker.GetMock<IModpackVersionProvider>();
		provider.SetupGet(x => x.InstalledModpackVersion).Returns(NuGetVersion.Parse("3.2.0"));
		provider.SetupAdd(x => x.PropertyChanged += It.IsAny<PropertyChangedEventHandler>());
		provider.SetupRemove(x => x.PropertyChanged -= It.IsAny<PropertyChangedEventHandler>());
		var updates = mocker.GetMock<IUpdateProvider>();
		updates.Setup(x => x.GetLauncherUpdateInfo(It.IsAny<CancellationToken>())).ReturnsAsync((LauncherUpdateInfo?)null);
		updates.Setup(x => x.GetModpackUpdateInfo(It.IsAny<CancellationToken>())).ReturnsAsync((ModpackUpdateInfo?)null);
		updates.Setup(x => x.GetAvailableModpackVersions(It.IsAny<CancellationToken>())).ReturnsAsync([]);
		var feed = mocker.GetMock<IFeedProvider>();
		feed.SetupGet(x => x.BetaAccessChanged).Returns(beta);
		feed.Setup(x => x.GetLauncherFeedItems(It.IsAny<CancellationToken>())).ReturnsAsync(
		[
			Launcher("1.3.0", "new.md"), Launcher("1.2.1", null), Launcher("1.2.0", "current.md"),
		]);
		mocker.GetMock<IAppVersionProvider>().SetupGet(x => x.Version).Returns(NuGetVersion.Parse("1.2.1"));
		mocker.Use(mocker.CreateInstance<GraphPacksViewModel>());
		mocker.Use(mocker.CreateInstance<OptionalModsViewModel>());
		return mocker;
	}

	private static LauncherFeedItem Launcher(string version, string? path) => new()
	{
		Version = NuGetVersion.Parse(version), DownloadInfo = new DownloadInfo { S3Path = version + ".exe" },
		ChangelogPath = path == null ? null : new() { ["en"] = path },
	};
}

internal sealed class HomeCardsDispatcher : IUiDispatcher
{
	public void Post(Action action) => action();
}
