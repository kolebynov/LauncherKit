using NuGet.Versioning;
using R3;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.Services;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Update;
using LauncherKit.Infrastructure;

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
		mocker.GetMock<IInstallAppTaskProvider>().Setup(x => x.CreateModInstallAppTask(mod, ModVersion, It.IsAny<CancellationToken>()))
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
	public async Task InstallMod_TaskCreationCancelled_ReleasesReservation()
	{
		// Arrange
		using var events = new Subject<AppTaskQueue.Event>();
		var mocker = CreateMocker(events);
		var mod = Mod("Lite");
		mocker.GetMock<IInstallAppTaskProvider>().Setup(x => x.CreateModInstallAppTask(mod, null, It.IsAny<CancellationToken>()))
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
		mocker.GetMock<IInstallAppTaskProvider>().Setup(x => x.CreateModInstallAppTask(mod, ModVersion, It.IsAny<CancellationToken>()))
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
		// Arrange: a failed feed reload serves the default feed, without categories.
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
			x => x.CreateModInstallAppTask(full, It.IsAny<NuGetVersion?>(), It.IsAny<CancellationToken>()), Times.Never);
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
		mocker.GetMock<IInstallAppTaskProvider>().Setup(x => x.CreateModInstallAppTask(mod, ModVersion, It.IsAny<CancellationToken>()))
			.ReturnsAsync(task);
		mocker.GetMock<IAppTaskQueue>().Setup(x => x.EnqueueAndWaitForCompletion(task, It.IsAny<CancellationToken>()))
			.Returns(completion);
	}

	private static AppTask Task(string id) => new(id, new AppTaskDescription.Custom(id), _ => [], [], null);

	private static ModFeedData Mod(string name) => new()
	{
		Name = name,
		CategoryId = "mods",
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
