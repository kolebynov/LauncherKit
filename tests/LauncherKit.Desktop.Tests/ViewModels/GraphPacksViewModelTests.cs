using System.ComponentModel;
using LauncherKit.Desktop.Services;
using LauncherKit.Desktop.ViewModels;
using NuGet.Versioning;
using R3;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.GraphPacks;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Utils;
using LauncherKit.Infrastructure;
using LauncherKit.TestSupport;

namespace LauncherKit.Desktop.Tests.ViewModels;

[NonParallelizable]
[TestFixture]
public sealed class GraphPacksViewModelTests
{
	[Test]
	public async Task GraphPacks_ModpackInstallationChain_StayUnavailableUntilLastChainTaskCompletes()
	{
		// Arrange
		using var events = new Subject<AppTaskQueue.Event>();
		var mocker = CreateMocker(events);
		using var sut = mocker.CreateInstance<GraphPacksViewModel>();
		using var activation = await ActivateWithCards(mocker, sut);
		var installModpack = new AppTask(AppTaskProvider.InstallModpackTaskId, new AppTaskDescription.Custom("STB"), _ => [], [], null);
		var installPatch = new AppTask($"{AppTaskProvider.InstallModpackPatchTaskIdPrefix}3.2.1", new AppTaskDescription.Custom("Patch"), _ => [], [], null);
		var installGraphPack = new AppTask($"{AppTaskProvider.InstallModpackComponentTaskIdPrefix}GraphPack", new AppTaskDescription.Custom("Graph pack"), _ => [], [], null);

		// Act
		events.OnNext(new AppTaskQueue.Event.TaskQueued(installModpack));
		events.OnNext(new AppTaskQueue.Event.TaskQueued(installPatch));
		events.OnNext(new AppTaskQueue.Event.TaskQueued(installGraphPack));
		events.OnNext(new AppTaskQueue.Event.TaskCompleted(installModpack));
		events.OnNext(new AppTaskQueue.Event.TaskCompleted(installPatch));
		var availableBeforeGraphPackTask = sut.GraphPacks.Single().IsAvailable;
		var unavailableWhileQueued = sut.GraphPacks.Single().UnavailableReason;
		events.OnNext(new AppTaskQueue.Event.TaskCompleted(installGraphPack));

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(availableBeforeGraphPackTask, Is.False);
			Assert.That(unavailableWhileQueued, Is.TypeOf<ModCardViewModel.InstallUnavailableReason.ModpackTaskQueued>());
			Assert.That(sut.GraphPacks.Single().IsAvailable, Is.True);
			Assert.That(sut.GraphPacks.Single().InstallRequiresNewGame, Is.True);
			Assert.That(sut.GraphPacks.Single().UpdateRequiresNewGame, Is.False);
			Assert.That(sut.GraphPacks.Single().UninstallRequiresNewGame, Is.True);
		});
	}

	[Test]
	public async Task GraphPacks_ModpackInstallationChainCancelled_BecomeAvailable()
	{
		// Arrange
		using var events = new Subject<AppTaskQueue.Event>();
		var mocker = CreateMocker(events);
		using var sut = mocker.CreateInstance<GraphPacksViewModel>();
		using var activation = await ActivateWithCards(mocker, sut);
		var installModpack = new AppTask(AppTaskProvider.InstallModpackTaskId, new AppTaskDescription.Custom("STB"), _ => [], [], null);
		var installGraphPack = new AppTask($"{AppTaskProvider.InstallModpackComponentTaskIdPrefix}GraphPack", new AppTaskDescription.Custom("Graph pack"), _ => [], [], null);
		events.OnNext(new AppTaskQueue.Event.TaskQueued(installModpack));
		events.OnNext(new AppTaskQueue.Event.TaskQueued(installGraphPack));
		var availableWhileQueued = sut.GraphPacks.Single().IsAvailable;

		// Act
		events.OnNext(new AppTaskQueue.Event.TaskRemoved(installModpack, 0));
		events.OnNext(new AppTaskQueue.Event.TaskRemoved(installGraphPack, 0));

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(availableWhileQueued, Is.False);
			Assert.That(sut.GraphPacks.Single().IsAvailable, Is.True);
		});
	}

	[Test]
	public async Task Card_InstallUpdateAndUninstall_QueueTasksDirectlyAndRefreshFacts()
	{
		using var events = new Subject<AppTaskQueue.Event>();
		var mocker = CreateMocker(events);
		var state = ModInstallState.NotInstalled;
		var installed = false;
		var install = new AppTask("InstallGraphPack", new AppTaskDescription.Custom("Install"), _ => [], [], null);
		var uninstall = new AppTask("UninstallGraphPack", new AppTaskDescription.Custom("Uninstall"), _ => [], [], null);
		mocker.GetMock<IInstallAppTaskProvider>()
			.Setup(x => x.GetModInstallState(It.Is<ModFeedData>(m => m.Name == "Lite"), NuGetVersion.Parse("3.2.0")))
			.Returns(() => state);
		mocker.GetMock<IModOrganizerService>().Setup(x => x.IsModInstalled("Lite")).Returns(() => installed);
		mocker.GetMock<IInstallAppTaskProvider>()
			.Setup(x => x.CreateModInstallAppTask(It.Is<ModFeedData>(m => m.Name == "Lite"), NuGetVersion.Parse("1.0.0"), It.IsAny<CancellationToken>()))
			.ReturnsAsync(install);
		mocker.GetMock<IInstallAppTaskProvider>()
			.Setup(x => x.CreateModUninstallAppTask(It.Is<ModFeedData>(m => m.Name == "Lite"))).Returns(uninstall);
		mocker.GetMock<IAppTaskQueue>().Setup(x => x.EnqueueAndWaitForCompletion(install, It.IsAny<CancellationToken>()))
			.Callback(() => { installed = true; state = ModInstallState.Installed(isUpdateAvailable: false, isUninstallSupported: true); })
			.Returns(Task.CompletedTask);
		mocker.GetMock<IAppTaskQueue>().Setup(x => x.EnqueueAndWaitForCompletion(uninstall, It.IsAny<CancellationToken>()))
			.Callback(() => { installed = false; state = ModInstallState.NotInstalled; }).Returns(Task.CompletedTask);
		using var sut = mocker.CreateInstance<GraphPacksViewModel>();
		using var activation = await ActivateWithCards(mocker, sut);
		var card = sut.GraphPacks.Single();
		await card.InstallCommand.ExecuteAsync(null);
		Assert.That(card.IsInstalled, Is.True);
		Assert.That(card.InstallRequiresNewGame, Is.False);
		state = ModInstallState.Installed(isUpdateAvailable: true, isUninstallSupported: true);
		mocker.GetMock<IModpackVersionProvider>().Raise(x => x.PropertyChanged += null,
			new PropertyChangedEventArgs(nameof(IModpackVersionProvider.InstalledModpackVersion)));
		await TaskAssert.Eventually(() => !ReferenceEquals(sut.GraphPacks.Single(), card), "updated graph pack card");
		card = sut.GraphPacks.Single();
		Assert.That(card.UpdateRequiresNewGame, Is.False);
		await card.InstallCommand.ExecuteAsync(null);
		Assert.That(card.IsUpdateAvailable, Is.False);
		await card.UninstallCommand.ExecuteAsync(null);
		Assert.That(card.IsInstalled, Is.False);
		mocker.GetMock<IAppTaskQueue>().Verify(x => x.EnqueueAndWaitForCompletion(install, It.IsAny<CancellationToken>()), Times.Exactly(2));
		mocker.GetMock<IAppTaskQueue>().Verify(x => x.EnqueueAndWaitForCompletion(uninstall, It.IsAny<CancellationToken>()), Times.Once);
	}


	private static AutoMocker CreateMocker(Subject<AppTaskQueue.Event> events)
	{
		var modpackVersion = NuGetVersion.Parse("3.2.0");
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<IUiDispatcher>(new HomeCardsDispatcher());
		mocker.GetMock<IAppTaskQueue>().SetupGet(x => x.Events).Returns(events);
		var modpackVersionProvider = mocker.GetMock<IModpackVersionProvider>();
		modpackVersionProvider.SetupGet(x => x.InstalledModpackVersion).Returns(modpackVersion);
		modpackVersionProvider.SetupAdd(x => x.PropertyChanged += It.IsAny<PropertyChangedEventHandler>());
		modpackVersionProvider.SetupRemove(x => x.PropertyChanged -= It.IsAny<PropertyChangedEventHandler>());
		mocker.GetMock<IFeedProvider>()
			.Setup(x => x.GetGraphPacks(It.IsAny<CancellationToken>()))
			.Returns(() => ValueTask.FromResult<IReadOnlyList<GraphPackFeedData>>(
			[
				new GraphPackFeedData
				{
					Name = "Lite",
					LogoPath = "lite.png",
					DescriptionPath = new() { ["ru"] = "lite.md" },
					Recommendation = new() { ["ru"] = "4 GB" },
					Items =
					[
						new ModFeedItem
						{
							Version = NuGetVersion.Parse("1.0.0"),
							ArchiveDownloadInfo = new DownloadInfo { S3Path = "lite.7z" },
							SupportedModpackVersion = VersionRange.Parse("[3.2.0, 3.3.0)"),
						},
					],
				},
			]));
		mocker.GetMock<IFeedUtils>().Setup(x => x.GetUri("lite.png")).Returns(new Uri("https://feed.test/lite.png"));
		mocker.GetMock<IFeedUtils>().Setup(x => x.GetUri("lite.md")).Returns(new Uri("https://feed.test/lite.md"));
		mocker.GetMock<IInstallAppTaskProvider>()
			.Setup(x => x.GetModInstallState(It.Is<ModFeedData>(mod => mod.Name == "Lite"), modpackVersion))
			.Returns(ModInstallState.NotInstalled);
		mocker.GetMock<IModOrganizerService>().Setup(x => x.IsModInstalled("Lite")).Returns(false);
		return mocker;
	}

	private static async Task<IDisposable> ActivateWithCards(AutoMocker mocker, GraphPacksViewModel sut)
	{
		var activation = await sut.Activator.Activate(CancellationToken.None);
		mocker.GetMock<IModpackVersionProvider>().Raise(
			x => x.PropertyChanged += null,
			new PropertyChangedEventArgs(nameof(IModpackVersionProvider.InstalledModpackVersion)));
		await TaskAssert.Eventually(() => sut.GraphPacks.Count == 1, "graph pack cards");
		return activation;
	}
}
