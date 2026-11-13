using System.ComponentModel;
using LauncherKit.Desktop.Services;
using LauncherKit.Desktop.ViewModels;
using LauncherKit.Infrastructure;
using NuGet.Versioning;
using R3;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Utils;
using LauncherKit.TestSupport;

namespace LauncherKit.Desktop.Tests.ViewModels;

[NonParallelizable]
[TestFixture]
public sealed class ModCategoryViewModelTests
{
	private static readonly NuGetVersion ModpackVersion = NuGetVersion.Parse("3.2.0");

	[Test]
	public async Task Activate_Categories_ListsFeedCategoriesInOrderWithoutLoadingMods()
	{
		// Arrange
		using var events = new Subject<AppTaskQueue.Event>();
		var mocker = CreateMocker(events, []);
		mocker.GetMock<IFeedProvider>().Setup(x => x.GetModCategories(It.IsAny<CancellationToken>())).ReturnsAsync(
		[
			Category("graph-packs", isExclusive: true, description: "[![Boosty](boosty.png)](https://boosty.test)"),
			Category("mods"),
		]);
		using var sut = mocker.CreateInstance<ModCategoriesViewModel>();

		// Act
		using var activation = await sut.Activator.Activate(CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.Categories.Select(x => x.Id), Is.EqualTo(new[] { "graph-packs", "mods" }));
			Assert.That(sut.Categories.Select(x => x.Title), Is.EqualTo(new[] { "Title graph-packs", "Title mods" }));
			Assert.That(sut.Categories.Select(x => x.Description),
				Is.EqualTo(new[] { "[![Boosty](boosty.png)](https://boosty.test)", null }));
			Assert.That(sut.Categories.Select(x => x.Mods.Count), Is.EqualTo(new[] { 0, 0 }));
		});
		mocker.GetMock<IFeedProvider>().Verify(x => x.GetModFeedDatas(It.IsAny<CancellationToken>()), Times.Never);
	}

	[Test]
	public async Task Activate_MixedCompatibility_ShowsCategoryModsAndHidesOnlyDiscontinuedUnsupported()
	{
		// Arrange
		using var events = new Subject<AppTaskQueue.Event>();
		var supported = Mod("Supported", "mods", "[3.0.0, 4.0.0)");
		var notUpdated = Mod("NotUpdated", "mods", "[2.0.0, 3.0.0)");
		var discontinued = Mod("Discontinued", "mods", "[2.0.0, 3.0.0)", isDiscontinued: true);
		var otherCategory = Mod("Other", "graph-packs", "[3.0.0, 4.0.0)");
		var mocker = CreateMocker(events, [supported, otherCategory, notUpdated, discontinued]);
		var sut = CreateCategory(mocker, Category("mods"), out var owner);

		// Act
		using var activation = await ActivateWithCards(mocker, sut, 2);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.Mods.Select(x => x.Name), Is.EqualTo(new[] { "Supported", "NotUpdated" }));
			Assert.That(sut.Mods.Select(x => x.IsAvailable), Is.EqualTo(new[] { true, false }));
			Assert.That(sut.Mods[1].UnavailableReason, Is.TypeOf<ModCardViewModel.InstallUnavailableReason.NotUpdated>());
			Assert.That(sut.Mods[0].InstallRequiresNewGame, Is.False);
			Assert.That(sut.Mods[0].UninstallRequiresNewGame, Is.False);
		});
		owner.Dispose();
	}

	[Test]
	public async Task Mods_ModpackInstallationChain_StayUnavailableUntilLastChainTaskCompletes()
	{
		// Arrange
		using var events = new Subject<AppTaskQueue.Event>();
		var mocker = CreateMocker(events, [Mod("Lite", "graph-packs", "[3.2.0, 3.3.0)", requiresNewGame: true)]);
		var sut = CreateCategory(mocker, Category("graph-packs", isExclusive: true), out var owner);
		using var activation = await ActivateWithCards(mocker, sut, 1);
		var installModpack = new AppTask(AppTaskProvider.InstallModpackTaskId, new AppTaskDescription.Custom("Modpack"), _ => [], [], null);
		var installPatch = new AppTask($"{AppTaskProvider.InstallModpackPatchTaskIdPrefix}3.2.1", new AppTaskDescription.Custom("Patch"), _ => [], [], null);
		var installComponent = new AppTask($"{AppTaskProvider.InstallModpackComponentTaskIdPrefix}GraphPack", new AppTaskDescription.Custom("Graph pack"), _ => [], [], null);

		// Act
		events.OnNext(new AppTaskQueue.Event.TaskQueued(installModpack));
		events.OnNext(new AppTaskQueue.Event.TaskQueued(installPatch));
		events.OnNext(new AppTaskQueue.Event.TaskQueued(installComponent));
		events.OnNext(new AppTaskQueue.Event.TaskCompleted(installModpack));
		events.OnNext(new AppTaskQueue.Event.TaskCompleted(installPatch));
		var availableBeforeComponentTask = sut.Mods.Single().IsAvailable;
		var unavailableWhileQueued = sut.Mods.Single().UnavailableReason;
		events.OnNext(new AppTaskQueue.Event.TaskCompleted(installComponent));

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(availableBeforeComponentTask, Is.False);
			Assert.That(unavailableWhileQueued, Is.TypeOf<ModCardViewModel.InstallUnavailableReason.ModpackTaskQueued>());
			Assert.That(sut.Mods.Single().IsAvailable, Is.True);
			Assert.That(sut.Mods.Single().InstallRequiresNewGame, Is.True);
			Assert.That(sut.Mods.Single().UpdateRequiresNewGame, Is.False);
			Assert.That(sut.Mods.Single().UninstallRequiresNewGame, Is.True);
		});
		owner.Dispose();
	}

	[Test]
	public async Task Mods_ModpackInstallationChainCancelled_BecomeAvailable()
	{
		// Arrange
		using var events = new Subject<AppTaskQueue.Event>();
		var mocker = CreateMocker(events, [Mod("Optional", "mods", "[3.2.0, 3.3.0)")]);
		var sut = CreateCategory(mocker, Category("mods"), out var owner);
		using var activation = await ActivateWithCards(mocker, sut, 1);
		var installModpack = new AppTask(AppTaskProvider.InstallModpackTaskId, new AppTaskDescription.Custom("Modpack"), _ => [], [], null);
		events.OnNext(new AppTaskQueue.Event.TaskQueued(installModpack));
		var availableWhileQueued = sut.Mods.Single().IsAvailable;

		// Act
		events.OnNext(new AppTaskQueue.Event.TaskRemoved(installModpack, 0));

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(availableWhileQueued, Is.False);
			Assert.That(sut.Mods.Single().IsAvailable, Is.True);
		});
		owner.Dispose();
	}

	[TestCase(true, false)]
	[TestCase(false, true)]
	public async Task Mods_OtherModOfCategoryInstalled_BlockOnlyInExclusiveCategory(bool isExclusive, bool expectedAvailable)
	{
		// Arrange
		using var events = new Subject<AppTaskQueue.Event>();
		var hiddenInstalled = Mod("Old", "graph-packs", "[2.0.0, 3.0.0)", isDiscontinued: true);
		var mocker = CreateMocker(events, [Mod("Lite", "graph-packs", "[3.2.0, 3.3.0)"), hiddenInstalled]);
		mocker.GetMock<IModOrganizerService>().Setup(x => x.IsModInstalled("Old")).Returns(true);
		var sut = CreateCategory(mocker, Category("graph-packs", isExclusive), out var owner);

		// Act
		using var activation = await ActivateWithCards(mocker, sut, 1);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.Mods.Single().IsAvailable, Is.EqualTo(expectedAvailable));
			Assert.That(sut.Mods.Single().UnavailableReason,
				Is.EqualTo(expectedAvailable ? null : new ModCardViewModel.InstallUnavailableReason.OtherModInstalled("Old")));
		});
		owner.Dispose();
	}

	[Test]
	public async Task Card_InstallUpdateAndUninstall_QueueTasksDirectlyAndRefreshFacts()
	{
		using var events = new Subject<AppTaskQueue.Event>();
		var mocker = CreateMocker(events, [Mod("Lite", "graph-packs", "[3.2.0, 3.3.0)", requiresNewGame: true)]);
		var state = ModInstallState.NotInstalled;
		var installed = false;
		var install = new AppTask("InstallLite", new AppTaskDescription.Custom("Install"), _ => [], [], null);
		var uninstall = new AppTask("UninstallLite", new AppTaskDescription.Custom("Uninstall"), _ => [], [], null);
		mocker.GetMock<IInstallAppTaskProvider>()
			.Setup(x => x.GetModInstallState(It.Is<ModFeedData>(m => m.Name == "Lite"), ModpackVersion))
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
		var sut = CreateCategory(mocker, Category("graph-packs", isExclusive: true), out var owner);
		using var activation = await ActivateWithCards(mocker, sut, 1);
		var card = sut.Mods.Single();
		await card.InstallCommand.ExecuteAsync(null);
		Assert.That(card.IsInstalled, Is.True);
		Assert.That(card.InstallRequiresNewGame, Is.False);
		state = ModInstallState.Installed(isUpdateAvailable: true, isUninstallSupported: true);
		mocker.GetMock<IModpackVersionProvider>().Raise(x => x.PropertyChanged += null,
			new PropertyChangedEventArgs(nameof(IModpackVersionProvider.InstalledModpackVersion)));
		await TaskAssert.Eventually(() => !ReferenceEquals(sut.Mods.Single(), card), "updated mod card");
		card = sut.Mods.Single();
		Assert.That(card.UpdateRequiresNewGame, Is.False);
		await card.InstallCommand.ExecuteAsync(null);
		Assert.That(card.IsUpdateAvailable, Is.False);
		await card.UninstallCommand.ExecuteAsync(null);
		Assert.That(card.IsInstalled, Is.False);
		mocker.GetMock<IAppTaskQueue>().Verify(x => x.EnqueueAndWaitForCompletion(install, It.IsAny<CancellationToken>()), Times.Exactly(2));
		mocker.GetMock<IAppTaskQueue>().Verify(x => x.EnqueueAndWaitForCompletion(uninstall, It.IsAny<CancellationToken>()), Times.Once);
		owner.Dispose();
	}

	[Test]
	public async Task Mods_ReactivatedDuringPendingExclusiveInstall_OtherModStaysBlockedUntilInstallEnds()
	{
		// Arrange
		using var events = new Subject<AppTaskQueue.Event>();
		var lite = Mod("Lite", "graph-packs", "[3.2.0, 3.3.0)");
		var mocker = CreateMocker(events, [lite, Mod("Full", "graph-packs", "[3.2.0, 3.3.0)")]);
		mocker.GetMock<IFeedProvider>().Setup(x => x.GetModCategories(It.IsAny<CancellationToken>()))
			.ReturnsAsync([Category("graph-packs", isExclusive: true)]);
		var install = new AppTask("InstallLite", new AppTaskDescription.Custom("Install"), _ => [], [], null);
		var installCompletion = new TaskCompletionSource();
		mocker.GetMock<IInstallAppTaskProvider>()
			.Setup(x => x.CreateModInstallAppTask(lite, NuGetVersion.Parse("1.0.0"), It.IsAny<CancellationToken>()))
			.ReturnsAsync(install);
		mocker.GetMock<IAppTaskQueue>().Setup(x => x.EnqueueAndWaitForCompletion(install, It.IsAny<CancellationToken>()))
			.Returns(installCompletion.Task);
		using var owner = mocker.CreateInstance<ModCategoriesViewModel>();
		var ownerActivation = await owner.Activator.Activate(CancellationToken.None);
		var category = owner.Categories.Single();
		var categoryActivation = await ActivateWithCards(mocker, category, 2);
		var installing = category.Mods[0].InstallCommand.ExecuteAsync(null);

		// Act
		categoryActivation.Dispose();
		ownerActivation.Dispose();
		using var reactivation = await owner.Activator.Activate(CancellationToken.None);
		var recreated = owner.Categories.Single();
		using var recreatedActivation = await ActivateWithCards(mocker, recreated, 2);
		var fullAvailableWhileInstalling = recreated.Mods[1].IsAvailable;
		var fullReasonWhileInstalling = recreated.Mods[1].UnavailableReason;
		installCompletion.SetResult();
		await installing;

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(recreated, Is.Not.SameAs(category));
			Assert.That(fullAvailableWhileInstalling, Is.False);
			Assert.That(fullReasonWhileInstalling,
				Is.EqualTo(new ModCardViewModel.InstallUnavailableReason.OtherModInstalled("Lite")));
			Assert.That(recreated.Mods[1].IsAvailable, Is.True);
		});
	}

	[Test]
	public async Task Mods_RecreatedCardRepeatsPendingExclusiveInstall_OtherModStaysBlocked()
	{
		// Arrange
		using var events = new Subject<AppTaskQueue.Event>();
		var lite = Mod("Lite", "graph-packs", "[3.2.0, 3.3.0)");
		var mocker = CreateMocker(events, [lite, Mod("Full", "graph-packs", "[3.2.0, 3.3.0)")]);
		mocker.GetMock<IFeedProvider>().Setup(x => x.GetModCategories(It.IsAny<CancellationToken>()))
			.ReturnsAsync([Category("graph-packs", isExclusive: true)]);
		var install = new AppTask("InstallLite", new AppTaskDescription.Custom("Install"), _ => [], [], null);
		var installCompletion = new TaskCompletionSource();
		var enqueueCount = 0;
		mocker.GetMock<IInstallAppTaskProvider>()
			.Setup(x => x.CreateModInstallAppTask(lite, NuGetVersion.Parse("1.0.0"), It.IsAny<CancellationToken>()))
			.ReturnsAsync(install);
		// The queue drops a duplicate of a queued task, so waiting for it completes at once.
		mocker.GetMock<IAppTaskQueue>().Setup(x => x.EnqueueAndWaitForCompletion(install, It.IsAny<CancellationToken>()))
			.Returns(() => enqueueCount++ == 0 ? installCompletion.Task : Task.CompletedTask);
		using var owner = mocker.CreateInstance<ModCategoriesViewModel>();
		var ownerActivation = await owner.Activator.Activate(CancellationToken.None);
		var category = owner.Categories.Single();
		var categoryActivation = await ActivateWithCards(mocker, category, 2);
		var installing = category.Mods[0].InstallCommand.ExecuteAsync(null);
		categoryActivation.Dispose();
		ownerActivation.Dispose();
		using var reactivation = await owner.Activator.Activate(CancellationToken.None);
		var recreated = owner.Categories.Single();
		using var recreatedActivation = await ActivateWithCards(mocker, recreated, 2);

		// Act
		if (recreated.Mods[0].InstallCommand.CanExecute(null))
			await recreated.Mods[0].InstallCommand.ExecuteAsync(null);
		var fullAvailable = recreated.Mods[1].IsAvailable;
		installCompletion.SetResult();
		await installing;

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(fullAvailable, Is.False);
			Assert.That(enqueueCount, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Mods_ExclusiveInstallCompletesAfterReactivation_RecreatedCardShowsInstalled()
	{
		// Arrange
		using var events = new Subject<AppTaskQueue.Event>();
		var lite = Mod("Lite", "graph-packs", "[3.2.0, 3.3.0)");
		var mocker = CreateMocker(events, [lite, Mod("Full", "graph-packs", "[3.2.0, 3.3.0)")]);
		mocker.GetMock<IFeedProvider>().Setup(x => x.GetModCategories(It.IsAny<CancellationToken>()))
			.ReturnsAsync([Category("graph-packs", isExclusive: true)]);
		var installed = false;
		mocker.GetMock<IInstallAppTaskProvider>().Setup(x => x.GetModInstallState(lite, ModpackVersion))
			.Returns(() => installed
				? ModInstallState.Installed(isUpdateAvailable: false, isUninstallSupported: true)
				: ModInstallState.NotInstalled);
		mocker.GetMock<IModOrganizerService>().Setup(x => x.IsModInstalled("Lite")).Returns(() => installed);
		var install = new AppTask("InstallLite", new AppTaskDescription.Custom("Install"), _ => [], [], null);
		var installCompletion = new TaskCompletionSource();
		mocker.GetMock<IInstallAppTaskProvider>()
			.Setup(x => x.CreateModInstallAppTask(lite, NuGetVersion.Parse("1.0.0"), It.IsAny<CancellationToken>()))
			.ReturnsAsync(install);
		mocker.GetMock<IAppTaskQueue>().Setup(x => x.EnqueueAndWaitForCompletion(install, It.IsAny<CancellationToken>()))
			.Returns(installCompletion.Task);
		using var owner = mocker.CreateInstance<ModCategoriesViewModel>();
		var ownerActivation = await owner.Activator.Activate(CancellationToken.None);
		var category = owner.Categories.Single();
		var categoryActivation = await ActivateWithCards(mocker, category, 2);
		var installing = category.Mods[0].InstallCommand.ExecuteAsync(null);
		categoryActivation.Dispose();
		ownerActivation.Dispose();
		using var reactivation = await owner.Activator.Activate(CancellationToken.None);
		var recreated = owner.Categories.Single();
		using var recreatedActivation = await ActivateWithCards(mocker, recreated, 2);

		// Act
		installed = true;
		installCompletion.SetResult();
		await installing;

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(recreated.Mods[0].IsInstalled, Is.True);
			Assert.That(recreated.Mods[0].InstallCommand.CanExecute(null), Is.False);
			Assert.That(recreated.Mods[0].UninstallCommand.CanExecute(null), Is.True);
			Assert.That(recreated.Mods[1].IsAvailable, Is.False);
			Assert.That(recreated.Mods[1].UnavailableReason,
				Is.EqualTo(new ModCardViewModel.InstallUnavailableReason.OtherModInstalled("Lite")));
		});
	}

	private static AutoMocker CreateMocker(Subject<AppTaskQueue.Event> events, IReadOnlyList<ModFeedData> mods)
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<IUiDispatcher>(HomeCardsDispatcher.Instance);
		mocker.GetMock<IAppTaskQueue>().SetupGet(x => x.Events).Returns(events);
		var modpackVersionProvider = mocker.GetMock<IModpackVersionProvider>();
		modpackVersionProvider.SetupGet(x => x.InstalledModpackVersion).Returns(ModpackVersion);
		modpackVersionProvider.SetupAdd(x => x.PropertyChanged += It.IsAny<PropertyChangedEventHandler>());
		modpackVersionProvider.SetupRemove(x => x.PropertyChanged -= It.IsAny<PropertyChangedEventHandler>());
		mocker.GetMock<IFeedProvider>().Setup(x => x.GetModFeedDatas(It.IsAny<CancellationToken>())).ReturnsAsync(mods);
		mocker.GetMock<IFeedUtils>().Setup(x => x.GetUri(It.IsAny<string>())).Returns<string>(p => new Uri("https://feed.test/" + p));
		foreach (var mod in mods)
		{
			mocker.GetMock<IInstallAppTaskProvider>().Setup(x => x.GetModInstallState(mod, ModpackVersion)).Returns(ModInstallState.NotInstalled);
			mocker.GetMock<IModOrganizerService>().Setup(x => x.IsModInstalled(mod.Name)).Returns(false);
		}

		return mocker;
	}

	private static ModCategoryViewModel CreateCategory(AutoMocker mocker, ModCategoryFeedData category, out ModCategoriesViewModel owner)
	{
		owner = mocker.CreateInstance<ModCategoriesViewModel>();
		return new ModCategoryViewModel(category, owner, mocker.Get<IInstallAppTaskProvider>(), mocker.Get<IAppTaskQueue>(),
			mocker.Get<IFeedProvider>(), mocker.Get<IModpackVersionProvider>(), mocker.Get<IModOrganizerService>(),
			mocker.Get<IFeedUtils>(), mocker.Get<IUiDispatcher>());
	}

	private static async Task<IDisposable> ActivateWithCards(AutoMocker mocker, ModCategoryViewModel sut, int count)
	{
		var activation = await sut.Activator.Activate(CancellationToken.None);
		mocker.GetMock<IModpackVersionProvider>().Raise(
			x => x.PropertyChanged += null,
			new PropertyChangedEventArgs(nameof(IModpackVersionProvider.InstalledModpackVersion)));
		await TaskAssert.Eventually(() => sut.Mods.Count == count, "mod cards");
		return activation;
	}

	private static ModCategoryFeedData Category(string id, bool isExclusive = false, string? description = null) => new()
	{
		Id = id,
		Title = new() { ["ru"] = $"Title {id}" },
		Description = description == null ? null : new() { ["ru"] = description },
		IsExclusive = isExclusive,
	};

	private static ModFeedData Mod(string name, string categoryId, string supportedVersion, bool isDiscontinued = false,
		bool requiresNewGame = false) => new()
	{
		Name = name,
		CategoryId = categoryId,
		LogoPath = $"{name}.png",
		DescriptionPath = new() { ["ru"] = $"{name}.md" },
		IsDiscontinued = isDiscontinued,
		RequiresNewGame = requiresNewGame,
		Items =
		[
			new ModFeedItem
			{
				Version = NuGetVersion.Parse("1.0.0"),
				ArchiveDownloadInfo = new DownloadInfo { S3Path = $"{name}.7z" },
				SupportedModpackVersion = VersionRange.Parse(supportedVersion),
			},
		],
	};
}
