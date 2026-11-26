using NuGet.Versioning;
using R3;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.ModDependencies;
using LauncherKit.Core.Services;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Update;
using LauncherKit.Infrastructure;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.Services;

[TestFixture]
public sealed class InstallationServiceTests
{
	private static readonly NuGetVersion ModVersion = NuGetVersion.Parse("1.0.0");

	[Test]
	public async Task InstallMod_QueuedUntilWaitEnds_ThenReportsFinished()
	{
		// Arrange
		using var events = new Subject<AppTaskQueue.Event>();
		var mocker = CreateMocker(events);
		var mod = Mod("Lite");
		var task = Task("Install_Lite");
		var completion = new TaskCompletionSource();
		mocker.GetMock<IInstallAppTaskProvider>().Setup(x => x.CreateModInstallWithDependenciesAppTask(mod, ModVersion, It.IsAny<ModDependencyReservations>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(task);
		mocker.GetMock<IAppTaskQueue>().Setup(x => x.EnqueueAndWaitForCompletion(task, It.IsAny<CancellationToken>()))
			.Returns(completion.Task);
		using var sut = mocker.CreateInstance<InstallationService>();
		var changes = new List<InstallationChange>();
		using var subscription = sut.Changes.Subscribe(changes.Add);

		// Act
		var installing = sut.InstallMod(mod, Category(), ModVersion, CancellationToken.None);
		var queuedWhileWaiting = sut.QueuedInstallations;
		completion.SetResult();
		await installing;

		// Assert
		var expected = new QueuedInstallation.ModInstall(mod);
		Assert.Multiple(() =>
		{
			Assert.That(queuedWhileWaiting, Is.EqualTo(new[] { expected }));
			Assert.That(sut.QueuedInstallations, Is.Empty);
			Assert.That(changes, Is.EqualTo(new InstallationChange[]
			{
				new InstallationChange.Queued(expected), new InstallationChange.Finished(expected),
			}));
		});
	}

	[Test]
	public async Task InstallMod_DependenciesReserved_TrackedUntilTheInstallationEndsAndOtherQueuedInstallsReported()
	{
		// Arrange
		using var events = new Subject<AppTaskQueue.Event>();
		var mocker = CreateMocker(events);
		var mod = Mod("Addon");
		var dependency = Mod("Library");
		var other = Mod("Other");
		SetupInstall(mocker, other, new TaskCompletionSource().Task);
		var task = Task("Install_Addon");
		var completion = new TaskCompletionSource();
		ModDependencyReservations? reservations = null;
		mocker.GetMock<IInstallAppTaskProvider>()
			.Setup(x => x.CreateModInstallWithDependenciesAppTask(mod, ModVersion, It.IsAny<ModDependencyReservations>(),
				It.IsAny<CancellationToken>()))
			.Callback((ModFeedData _, NuGetVersion? _, ModDependencyReservations r, CancellationToken _) => reservations = r)
			.ReturnsAsync(task);
		mocker.GetMock<IAppTaskQueue>().Setup(x => x.EnqueueAndWaitForCompletion(task, It.IsAny<CancellationToken>()))
			.Returns(completion.Task);
		using var sut = mocker.CreateInstance<InstallationService>();
		_ = sut.InstallMod(other, Category(), ModVersion, CancellationToken.None);
		var installing = sut.InstallMod(mod, Category(), ModVersion, CancellationToken.None);

		// Act
		var queuedInstalls = reservations!.GetQueuedInstalls();
		var conflict = reservations.TryReserve([dependency, other], [Category()]);
		var queuedWhileWaiting = sut.QueuedInstallations;
		completion.SetResult();
		await installing;

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(queuedInstalls, Is.EqualTo(new[] { "Other" }));
			Assert.That(conflict, Is.Null);
			Assert.That(queuedWhileWaiting, Is.EqualTo(new QueuedInstallation[]
			{
				new QueuedInstallation.ModInstall(other), new QueuedInstallation.ModInstall(mod),
				new QueuedInstallation.ModInstall(dependency),
			}));
			Assert.That(sut.QueuedInstallations, Is.EqualTo(new[] { new QueuedInstallation.ModInstall(other) }));
		});
	}

	[Test]
	public async Task InstallMod_InstallationRemovedWhileResolvingDependencies_EndsResolutionAndFreesQueue()
	{
		// Arrange
		using var queue = CreateQueue();
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<IAppTaskQueue>(queue);
		var addon = Mod("Addon");
		var resolving = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var resolutionEnded = new TaskCompletionSource<ModReservationConflict?>(TaskCreationOptions.RunContinuationsAsynchronously);
		SetupDependencyInstall(mocker, addon, async reservations =>
		{
			resolving.SetResult();
			try
			{
				await System.Threading.Tasks.Task.Delay(Timeout.Infinite, reservations.RequestEnded);
			}
			catch (OperationCanceledException)
			{
			}

			resolutionEnded.SetResult(reservations.TryReserve([Mod("Library")], [Category()]));
		});
		using var sut = mocker.CreateInstance<InstallationService>();
		var installing = sut.InstallMod(addon, Category(), ModVersion, CancellationToken.None);
		await TaskAssert.WithinTimeout(resolving.Task);

		// Act
		await queue.CancelAppTask("Install_Addon", CancellationToken.None);
		await TaskAssert.WithinTimeout(installing);
		var conflict = await TaskAssert.WithinTimeout(resolutionEnded.Task);
		await TaskAssert.WithinTimeout(queue.EnqueueAndWaitForCompletion(Task("Next"), CancellationToken.None));

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(conflict, Is.Null);
			Assert.That(sut.QueuedInstallations, Is.Empty);
			Assert.That(queue.TaskCount, Is.Zero);
		});
	}

	[Test]
	public async Task InstallMod_ExclusivePeerQueuedWhileResolving_ReportsConflictAndReservesNothing()
	{
		// Arrange
		using var queue = CreateQueue();
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<IAppTaskQueue>(queue);
		var addon = Mod("Addon");
		var pack = Mod("Pack", "packs");
		var otherPack = Mod("Other Pack", "packs");
		var packs = PacksCategory();
		var resolving = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var reserved = new TaskCompletionSource<(ModReservationConflict?, IReadOnlyList<QueuedInstallation>)>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		InstallationService? service = null;
		SetupDependencyInstall(mocker, addon, async reservations =>
		{
			resolving.SetResult();
			await gate.Task;
			reserved.SetResult((reservations.TryReserve([pack], [Category(), packs]), service!.QueuedInstallations));
		});
		mocker.GetMock<IInstallAppTaskProvider>()
			.Setup(x => x.CreateModInstallWithDependenciesAppTask(otherPack, ModVersion, It.IsAny<ModDependencyReservations>(),
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(Task("Install_Other Pack"));
		using var sut = mocker.CreateInstance<InstallationService>();
		service = sut;
		var installing = sut.InstallMod(addon, Category(), ModVersion, CancellationToken.None);
		await TaskAssert.WithinTimeout(resolving.Task);
		var installingPeer = sut.InstallMod(otherPack, packs, ModVersion, CancellationToken.None);

		// Act
		gate.SetResult();
		var (conflict, queuedAfterReserving) = await TaskAssert.WithinTimeout(reserved.Task);
		await TaskAssert.WithinTimeout(installing);
		await TaskAssert.WithinTimeout(installingPeer);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(conflict, Is.EqualTo(new ModReservationConflict(pack, otherPack)));
			Assert.That(queuedAfterReserving, Is.EqualTo(new QueuedInstallation[]
			{
				new QueuedInstallation.ModInstall(addon), new QueuedInstallation.ModInstall(otherPack),
			}));
		});
	}

	[Test]
	public async Task InstallMod_DependencyOfExclusiveCategoryReserved_RefusesDirectInstallOfItsPeer()
	{
		// Arrange
		using var queue = CreateQueue();
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<IAppTaskQueue>(queue);
		var addon = Mod("Addon");
		var pack = Mod("Pack", "packs");
		var otherPack = Mod("Other Pack", "packs");
		var packs = PacksCategory();
		var reserved = new TaskCompletionSource<ModReservationConflict?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		SetupDependencyInstall(mocker, addon, async reservations =>
		{
			reserved.SetResult(reservations.TryReserve([pack], [Category(), packs]));
			await gate.Task;
		});
		using var sut = mocker.CreateInstance<InstallationService>();
		var installing = sut.InstallMod(addon, Category(), ModVersion, CancellationToken.None);
		var conflict = await TaskAssert.WithinTimeout(reserved.Task);

		// Act
		await TaskAssert.WithinTimeout(sut.InstallMod(otherPack, packs, ModVersion, CancellationToken.None));
		var queued = sut.QueuedInstallations;
		gate.SetResult();
		await TaskAssert.WithinTimeout(installing);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(conflict, Is.Null);
			Assert.That(queued, Is.EqualTo(new QueuedInstallation[]
			{
				new QueuedInstallation.ModInstall(addon), new QueuedInstallation.ModInstall(pack),
			}));
			mocker.GetMock<IInstallAppTaskProvider>().Verify(x => x.CreateModInstallWithDependenciesAppTask(otherPack,
				It.IsAny<NuGetVersion?>(), It.IsAny<ModDependencyReservations>(), It.IsAny<CancellationToken>()), Times.Never);
		});
	}

	[Test]
	public async Task InstallMod_SubscriberRequestsPeerWhenFirstDependencyIsReported_SeesTheWholePlanAndIsRefused()
	{
		// Arrange
		using var queue = CreateQueue();
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<IAppTaskQueue>(queue);
		var addon = Mod("Addon");
		var library = Mod("Library");
		var pack = Mod("Pack", "packs");
		var otherPack = Mod("Other Pack", "packs");
		var packs = PacksCategory();
		ModReservationConflict? conflict = null;
		SetupDependencyInstall(mocker, addon, reservations =>
		{
			conflict = reservations.TryReserve([library, pack], [Category(), packs]);
			return System.Threading.Tasks.Task.CompletedTask;
		});
		mocker.GetMock<IInstallAppTaskProvider>()
			.Setup(x => x.CreateModInstallWithDependenciesAppTask(otherPack, ModVersion, It.IsAny<ModDependencyReservations>(),
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(Task("Install_Other Pack"));
		using var sut = mocker.CreateInstance<InstallationService>();
		IReadOnlyList<QueuedInstallation>? seen = null;
		Task? peerRequest = null;
		using var subscription = sut.Changes.Subscribe(change =>
		{
			// Synchronously, under the service's lock, like any subscriber.
			if (peerRequest == null && change is InstallationChange.Queued(var installation) &&
			    installation is QueuedInstallation.ModInstall(var mod) && mod == library)
			{
				seen = sut.QueuedInstallations;
				peerRequest = sut.InstallMod(otherPack, packs, ModVersion, CancellationToken.None);
			}
		});

		// Act
		await TaskAssert.WithinTimeout(sut.InstallMod(addon, Category(), ModVersion, CancellationToken.None));
		await TaskAssert.WithinTimeout(peerRequest!);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(conflict, Is.Null);
			Assert.That(seen, Is.EqualTo(new QueuedInstallation[]
			{
				new QueuedInstallation.ModInstall(addon), new QueuedInstallation.ModInstall(library),
				new QueuedInstallation.ModInstall(pack),
			}));
			mocker.GetMock<IInstallAppTaskProvider>().Verify(x => x.CreateModInstallWithDependenciesAppTask(otherPack,
				It.IsAny<NuGetVersion?>(), It.IsAny<ModDependencyReservations>(), It.IsAny<CancellationToken>()), Times.Never);
		});
	}

	[Test]
	public async Task InstallMod_TaskCreationCancelled_ReleasesReservation()
	{
		// Arrange
		using var events = new Subject<AppTaskQueue.Event>();
		var mocker = CreateMocker(events);
		var mod = Mod("Lite");
		mocker.GetMock<IInstallAppTaskProvider>().Setup(x => x.CreateModInstallWithDependenciesAppTask(mod, null, It.IsAny<ModDependencyReservations>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(new OperationCanceledException());
		using var sut = mocker.CreateInstance<InstallationService>();

		// Act
		await Assert.ThrowsAsync<OperationCanceledException>(() => sut.InstallMod(mod, Category(), null, CancellationToken.None));

		// Assert
		Assert.That(sut.QueuedInstallations, Is.Empty);
	}

	[Test]
	public async Task InstallAndUninstallMod_SameModQueued_NeitherQueuesNorReleasesTheFirstRequest()
	{
		// Arrange
		using var events = new Subject<AppTaskQueue.Event>();
		var mocker = CreateMocker(events);
		var mod = Mod("Lite");
		var task = Task("Install_Lite");
		var completion = new TaskCompletionSource();
		mocker.GetMock<IInstallAppTaskProvider>().Setup(x => x.CreateModInstallWithDependenciesAppTask(mod, ModVersion, It.IsAny<ModDependencyReservations>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(task);
		mocker.GetMock<IAppTaskQueue>().Setup(x => x.EnqueueAndWaitForCompletion(task, It.IsAny<CancellationToken>()))
			.Returns(completion.Task);
		using var sut = mocker.CreateInstance<InstallationService>();
		var installing = sut.InstallMod(mod, Category(), ModVersion, CancellationToken.None);

		// Act
		await sut.InstallMod(Mod("Lite"), Category(), ModVersion, CancellationToken.None);
		await sut.UninstallMod(mod, CancellationToken.None);
		var queuedAfterDuplicates = sut.QueuedInstallations;
		completion.SetResult();
		await installing;

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(queuedAfterDuplicates, Is.EqualTo(new[] { new QueuedInstallation.ModInstall(mod) }));
			Assert.That(sut.QueuedInstallations, Is.Empty);
		});
		mocker.GetMock<IAppTaskQueue>().Verify(x => x.EnqueueAndWaitForCompletion(task, It.IsAny<CancellationToken>()), Times.Once);
	}

	[Test]
	public async Task InstallMod_DifferentMods_AreTrackedIndependently()
	{
		// Arrange
		using var events = new Subject<AppTaskQueue.Event>();
		var mocker = CreateMocker(events);
		var lite = Mod("Lite");
		var full = Mod("Full");
		var liteCompletion = new TaskCompletionSource();
		var fullCompletion = new TaskCompletionSource();
		SetupInstall(mocker, lite, liteCompletion.Task);
		SetupInstall(mocker, full, fullCompletion.Task);
		using var sut = mocker.CreateInstance<InstallationService>();
		var installingLite = sut.InstallMod(lite, Category(), ModVersion, CancellationToken.None);
		var installingFull = sut.InstallMod(full, Category(), ModVersion, CancellationToken.None);

		// Act
		liteCompletion.SetResult();
		await installingLite;

		// Assert
		Assert.That(sut.QueuedInstallations, Is.EqualTo(new[] { new QueuedInstallation.ModInstall(full) }));
		fullCompletion.SetResult();
		await installingFull;
		Assert.That(sut.QueuedInstallations, Is.Empty);
	}

	[Test]
	public async Task ModpackChain_StaysQueuedThroughFailureAndSkippedTasksUntilLastTaskLeaves()
	{
		// Arrange
		using var events = new Subject<AppTaskQueue.Event>();
		var mocker = CreateMocker(events);
		var steps = new List<string>();
		mocker.GetMock<IModpackVersionProvider>().Setup(x => x.RefreshInstalledModpackVersion())
			.Callback(() => steps.Add("refresh"));
		using var sut = mocker.CreateInstance<InstallationService>();
		using var subscription = sut.Changes.Subscribe(x => steps.Add(x.GetType().Name));
		var select = Task(AppTaskProvider.SelectModpackComponentsTaskId);
		var modpack = Task(AppTaskProvider.InstallModpackTaskId);
		var patch = Task($"{AppTaskProvider.InstallModpackPatchTaskIdPrefix}3.2.1");
		var component = Task($"{AppTaskProvider.InstallModpackComponentTaskIdPrefix}GraphPack");
		var runGame = Task("RunGame");

		// Act: a game launch queues the chain as its dependencies.
		foreach (var task in new[] { select, modpack, patch, component, runGame })
			events.OnNext(new AppTaskQueue.Event.TaskQueued(task));
		events.OnNext(new AppTaskQueue.Event.TaskCompleted(select));
		events.OnNext(new AppTaskQueue.Event.TaskFailed(modpack, new IOException("locked")));
		var queuedWhileFailed = sut.QueuedInstallations;
		// Resumed: the skipped component is removed first, and reported completed after its dependencies.
		events.OnNext(new AppTaskQueue.Event.TaskRemoved(component, 2));
		events.OnNext(new AppTaskQueue.Event.TaskCompleted(modpack));
		events.OnNext(new AppTaskQueue.Event.TaskCompleted(patch));
		var queuedAfterLastTask = sut.QueuedInstallations;
		events.OnNext(new AppTaskQueue.Event.TaskCompleted(component));
		events.OnNext(new AppTaskQueue.Event.TaskCompleted(runGame));

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(queuedWhileFailed, Is.EqualTo(new[] { QueuedInstallation.Modpack.Instance }));
			Assert.That(queuedAfterLastTask, Is.Empty);
			Assert.That(steps, Is.EqualTo(new[] { nameof(InstallationChange.Queued), "refresh", nameof(InstallationChange.Finished) }));
		});
	}

	[Test]
	public void ModpackChain_MoreEventsThanQueueReplays_TracksEveryTask()
	{
		// Arrange
		using var events = new Subject<AppTaskQueue.Event>();
		var mocker = CreateMocker(events);
		mocker.GetMock<IModpackVersionProvider>().Setup(x => x.RefreshInstalledModpackVersion());
		using var sut = mocker.CreateInstance<InstallationService>();
		var patches = Enumerable.Range(0, 12).Select(i => Task($"{AppTaskProvider.InstallModpackPatchTaskIdPrefix}3.2.{i}")).ToArray();

		// Act
		foreach (var patch in patches)
			events.OnNext(new AppTaskQueue.Event.TaskQueued(patch));
		foreach (var patch in patches[..^1])
			events.OnNext(new AppTaskQueue.Event.TaskCompleted(patch));

		// Assert
		Assert.That(sut.QueuedInstallations, Is.EqualTo(new[] { QueuedInstallation.Modpack.Instance }));
		events.OnNext(new AppTaskQueue.Event.TaskCompleted(patches[^1]));
		Assert.That(sut.QueuedInstallations, Is.Empty);
	}

	[Test]
	public void ModpackChain_OtherTasks_AreNotTracked()
	{
		using var events = new Subject<AppTaskQueue.Event>();
		var mocker = CreateMocker(events);
		using var sut = mocker.CreateInstance<InstallationService>();

		events.OnNext(new AppTaskQueue.Event.TaskQueued(Task("UpdateLauncher")));

		Assert.That(sut.QueuedInstallations, Is.Empty);
	}

	[Test]
	public async Task InstallAndUninstallModpack_RefreshVersionAfterWaitEvenOnFailure()
	{
		// Arrange
		using var events = new Subject<AppTaskQueue.Event>();
		var mocker = CreateMocker(events);
		var install = Task(AppTaskProvider.InstallModpackTaskId);
		var uninstall = Task(AppTaskProvider.UninstallModpackTaskId);
		mocker.GetMock<IAppTaskQueue>().Setup(x => x.EnqueueAndWaitForCompletion(install, It.IsAny<CancellationToken>()))
			.Returns(System.Threading.Tasks.Task.CompletedTask);
		mocker.GetMock<IAppTaskProvider>().Setup(x => x.CreateUninstallModpackTask()).Returns(uninstall);
		mocker.GetMock<IAppTaskQueue>().Setup(x => x.EnqueueAndWaitForCompletion(uninstall, It.IsAny<CancellationToken>()))
			.ThrowsAsync(new IOException("locked"));
		mocker.GetMock<IModpackVersionProvider>().Setup(x => x.RefreshInstalledModpackVersion());
		using var sut = mocker.CreateInstance<InstallationService>();

		// Act
		await sut.InstallModpack(new ModpackUpdateInfo
		{
			UpdateAppTask = install, UpdateType = UpdateType.InitialInstall.Instance, Version = NuGetVersion.Parse("3.2.0"),
		}, CancellationToken.None);
		await Assert.ThrowsAsync<IOException>(() => sut.UninstallModpack(CancellationToken.None));

		// Assert
		mocker.GetMock<IModpackVersionProvider>().Verify(x => x.RefreshInstalledModpackVersion(), Times.Exactly(2));
	}

	[Test]
	public async Task RunGame_QueuesTheRunGameTask()
	{
		using var events = new Subject<AppTaskQueue.Event>();
		var mocker = CreateMocker(events);
		var launch = new GameLaunch("Skyrim", null);
		var task = Task("RunGame");
		mocker.GetMock<IAppTaskProvider>().Setup(x => x.CreateRunGameTask(launch, It.IsAny<CancellationToken>())).ReturnsAsync(task);
		mocker.GetMock<IAppTaskQueue>().Setup(x => x.EnqueueAndWaitForCompletion(task, It.IsAny<CancellationToken>()))
			.Returns(System.Threading.Tasks.Task.CompletedTask);
		using var sut = mocker.CreateInstance<InstallationService>();

		await sut.RunGame(launch, CancellationToken.None);

		mocker.GetMock<IAppTaskQueue>().Verify(x => x.EnqueueAndWaitForCompletion(task, It.IsAny<CancellationToken>()), Times.Once);
	}

	[TestCase(true, false)]
	[TestCase(false, true)]
	public async Task InstallMod_SiblingInstallQueued_SecondQueuedOnlyOutsideExclusiveCategory(bool isExclusive,
		bool expectSecondQueued)
	{
		// Arrange
		using var events = new Subject<AppTaskQueue.Event>();
		var mocker = CreateMocker(events);
		var category = Category(isExclusive);
		var lite = Mod("Lite");
		var full = Mod("Full");
		var liteCompletion = new TaskCompletionSource();
		var fullCompletion = new TaskCompletionSource();
		SetupInstall(mocker, lite, liteCompletion.Task);
		SetupInstall(mocker, full, fullCompletion.Task);
		using var sut = mocker.CreateInstance<InstallationService>();
		var installingLite = sut.InstallMod(lite, category, ModVersion, CancellationToken.None);

		// Act
		var installingFull = sut.InstallMod(full, category, ModVersion, CancellationToken.None);
		var queued = sut.QueuedInstallations;
		liteCompletion.SetResult();
		fullCompletion.SetResult();
		await installingLite;
		await installingFull;

		// Assert
		var expected = expectSecondQueued
			? new QueuedInstallation[] { new QueuedInstallation.ModInstall(lite), new QueuedInstallation.ModInstall(full) }
			: [new QueuedInstallation.ModInstall(lite)];
		Assert.Multiple(() =>
		{
			Assert.That(queued, Is.EqualTo(expected));
			Assert.That(sut.QueuedInstallations, Is.Empty);
		});
		mocker.GetMock<IAppTaskQueue>().Verify(x => x.EnqueueAndWaitForCompletion(
				It.Is<AppTask>(t => t.Id == "Install_Full"), It.IsAny<CancellationToken>()),
			expectSecondQueued ? Times.Once() : Times.Never());
	}

	[Test]
	public async Task InstallMod_ExclusiveSiblingWhileFeedHasNoCategories_IsRejectedWithoutReadingTheFeed()
	{
		// Arrange: a failed first feed load serves the default feed, without categories.
		using var events = new Subject<AppTaskQueue.Event>();
		var mocker = CreateMocker(events);
		var feedProvider = mocker.GetMock<IFeedProvider>();
		feedProvider.Setup(x => x.GetModCategories(It.IsAny<CancellationToken>())).ReturnsAsync([]);
		var category = Category(isExclusive: true);
		var lite = Mod("Lite");
		var full = Mod("Full");
		var liteCompletion = new TaskCompletionSource();
		SetupInstall(mocker, lite, liteCompletion.Task);
		using var sut = mocker.CreateInstance<InstallationService>();
		var installingLite = sut.InstallMod(lite, category, ModVersion, CancellationToken.None);

		// Act
		await sut.InstallMod(full, category, ModVersion, CancellationToken.None);
		var queued = sut.QueuedInstallations;
		liteCompletion.SetResult();
		await installingLite;

		// Assert
		Assert.That(queued, Is.EqualTo(new[] { new QueuedInstallation.ModInstall(lite) }));
		feedProvider.Verify(x => x.GetModCategories(It.IsAny<CancellationToken>()), Times.Never);
		mocker.GetMock<IInstallAppTaskProvider>().Verify(
			x => x.CreateModInstallWithDependenciesAppTask(full, It.IsAny<NuGetVersion?>(), It.IsAny<ModDependencyReservations>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Test]
	public async Task ModpackVersionRefreshes_FromWaitAndChainEnd_NeverOverlap()
	{
		// Arrange
		using var events = new Subject<AppTaskQueue.Event>();
		var mocker = CreateMocker(events);
		var install = Task(AppTaskProvider.InstallModpackTaskId);
		mocker.GetMock<IAppTaskQueue>().Setup(x => x.EnqueueAndWaitForCompletion(install, It.IsAny<CancellationToken>()))
			.Returns(System.Threading.Tasks.Task.CompletedTask);
		using var firstRefreshEntered = new ManualResetEventSlim();
		using var releaseFirstRefresh = new ManualResetEventSlim();
		var running = 0;
		var maxRunning = 0;
		var refreshes = 0;
		mocker.GetMock<IModpackVersionProvider>().Setup(x => x.RefreshInstalledModpackVersion()).Callback(() =>
		{
			var current = Interlocked.Increment(ref running);
			InterlockedMax(ref maxRunning, current);
			if (Interlocked.Increment(ref refreshes) == 1)
			{
				firstRefreshEntered.Set();
				releaseFirstRefresh.Wait(TimeSpan.FromSeconds(10));
			}

			Interlocked.Decrement(ref running);
		});
		using var sut = mocker.CreateInstance<InstallationService>();
		var finished = 0;
		using var subscription = sut.Changes.Subscribe(x =>
		{
			if (x is InstallationChange.Finished)
				Interlocked.Increment(ref finished);
		});
		events.OnNext(new AppTaskQueue.Event.TaskQueued(install));

		// Act: the waiting install refreshes while the queue thread ends the chain.
		var installing = System.Threading.Tasks.Task.Run(() => sut.InstallModpack(new ModpackUpdateInfo
		{
			UpdateAppTask = install, UpdateType = UpdateType.InitialInstall.Instance, Version = NuGetVersion.Parse("3.2.0"),
		}, CancellationToken.None));
		var firstEntered = firstRefreshEntered.Wait(TimeSpan.FromSeconds(10));
		var chainEnd = new Thread(() => events.OnNext(new AppTaskQueue.Event.TaskCompleted(install)));
		chainEnd.Start();
		// Nothing on the chain end's path blocks before the refresh lock, so once its thread waits, it waits there; without
		// the lock it would instead enter the second refresh (or finish), which also ends the spin.
		var chainEndSettled = SpinWait.SpinUntil(() =>
			(chainEnd.ThreadState & (ThreadState.WaitSleepJoin | ThreadState.Stopped)) != 0 || Volatile.Read(ref refreshes) > 1,
			TimeSpan.FromSeconds(10));
		var chainEndBlocked = (chainEnd.ThreadState & ThreadState.WaitSleepJoin) != 0;
		var refreshesWhileBlocked = Volatile.Read(ref refreshes);
		var finishedWhileBlocked = Volatile.Read(ref finished);
		releaseFirstRefresh.Set();
		await installing;
		chainEnd.Join();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(firstEntered, Is.True);
			Assert.That(chainEndSettled, Is.True);
			Assert.That(chainEndBlocked, Is.True);
			Assert.That(refreshesWhileBlocked, Is.EqualTo(1));
			Assert.That(finishedWhileBlocked, Is.Zero);
			Assert.That(refreshes, Is.EqualTo(2));
			Assert.That(maxRunning, Is.EqualTo(1));
			Assert.That(sut.QueuedInstallations, Is.Empty);
		});
	}

	[Test]
	public void Dispose_StopsTrackingQueueEvents()
	{
		using var events = new Subject<AppTaskQueue.Event>();
		var mocker = CreateMocker(events);
		var sut = mocker.CreateInstance<InstallationService>();

		sut.Dispose();
		events.OnNext(new AppTaskQueue.Event.TaskQueued(Task(AppTaskProvider.InstallModpackTaskId)));

		Assert.That(sut.QueuedInstallations, Is.Empty);
	}

	private static AutoMocker CreateMocker(Subject<AppTaskQueue.Event> events)
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IAppTaskQueue>().SetupGet(x => x.Events).Returns(events);
		return mocker;
	}

	private static ModCategoryFeedData Category(bool isExclusive = false) =>
		new() { Id = "mods", Title = new() { ["ru"] = "Mods" }, IsExclusive = isExclusive };

	private static void InterlockedMax(ref int target, int value)
	{
		int current;
		while (value > (current = Volatile.Read(ref target)) &&
		       Interlocked.CompareExchange(ref target, value, current) != current)
		{
		}
	}

	private static void SetupInstall(AutoMocker mocker, ModFeedData mod, Task completion)
	{
		var task = Task($"Install_{mod.Name}");
		mocker.GetMock<IInstallAppTaskProvider>().Setup(x => x.CreateModInstallWithDependenciesAppTask(mod, ModVersion, It.IsAny<ModDependencyReservations>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(task);
		mocker.GetMock<IAppTaskQueue>().Setup(x => x.EnqueueAndWaitForCompletion(task, It.IsAny<CancellationToken>()))
			.Returns(completion);
	}

	private static AppTask Task(string id) => new(id, new AppTaskDescription.Custom(id), _ => [], [], null);

	/// <summary>A real queue running tasks without microtasks of their own.</summary>
	private static AppTaskQueue CreateQueue()
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<Microsoft.Extensions.Logging.ILogger<AppTaskQueue>>(
			Microsoft.Extensions.Logging.Abstractions.NullLogger<AppTaskQueue>.Instance);
		mocker.Use(TimeProvider.System);
		ServiceScopeFactoryStub.ScopeFactoryFor(mocker, mocker.GetMock<IMicroTasksFactory>().Object);
		return mocker.CreateInstance<AppTaskQueue>();
	}

	/// <summary>
	/// Makes the installation of <paramref name="mod"/> a resolution task running <paramref name="resolve"/> with the
	/// request's reservations, followed by an installation task without microtasks.
	/// </summary>
	private static void SetupDependencyInstall(AutoMocker mocker, ModFeedData mod,
		Func<ModDependencyReservations, Task> resolve) =>
		mocker.GetMock<IInstallAppTaskProvider>()
			.Setup(x => x.CreateModInstallWithDependenciesAppTask(mod, ModVersion, It.IsAny<ModDependencyReservations>(),
				It.IsAny<CancellationToken>()))
			.Returns((ModFeedData _, NuGetVersion? _, ModDependencyReservations reservations, CancellationToken _) =>
			{
				var resolveTask = new AppTask($"ResolveModDependencies_{mod.Name}",
					new AppTaskDescription.Custom("resolve"), _ => [], [], null)
				{
					OnComplete = _ => new ValueTask(resolve(reservations)),
				};
				return ValueTask.FromResult(new AppTask($"Install_{mod.Name}", new AppTaskDescription.Custom("install"),
					_ => [], [resolveTask], null));
			});

	private static ModCategoryFeedData PacksCategory() =>
		new() { Id = "packs", Title = new() { ["ru"] = "Packs" }, IsExclusive = true };

	private static ModFeedData Mod(string name, string categoryId = "mods") => new()
	{
		Name = name,
		CategoryId = categoryId,
		LogoPath = $"{name}.png",
		DescriptionPath = new() { ["ru"] = $"{name}.md" },
		Items =
		[
			new ModFeedItem
			{
				Version = ModVersion,
				ArchiveDownloadInfo = new DownloadInfo { S3Path = $"{name}.7z" },
				SupportedModpackVersion = VersionRange.Parse("[3.0.0, 4.0.0)"),
			},
		],
	};
}
