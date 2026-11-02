using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using R3;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.Initialization;
using LauncherKit.Core.UserSettings;
using LauncherKit.Infrastructure;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.Initialization;

[TestFixture]
public sealed class CleanupOldDataInitializerTests
{
	[Test]
	public async Task Initialize_Always_QueuesSingleCleanupTask()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<Microsoft.Extensions.Logging.ILogger<CleanupOldDataInitializer>>(
			NullLogger<CleanupOldDataInitializer>.Instance);
		mocker.Use(TimeProvider.System);
		OptionsStubs.Options(mocker, new DownloadSettings { FileStoragePeriod = FileStoragePeriod.Week });
		AppTask? queued = null;
		mocker.GetMock<IAppTaskQueue>()
			.Setup(x => x.Enqueue(It.IsAny<AppTask>(), CancellationToken.None))
			.Callback<AppTask, CancellationToken>((task, _) => queued = task)
			.ReturnsAsync(AppTaskEnqueueResult.NotEnqueued.Instance);
		var sut = mocker.CreateInstance<CleanupOldDataInitializer>(enablePrivate: true);

		// Act
		await sut.Initialize();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(queued, Is.Not.Null);
			Assert.That(queued!.Id, Is.EqualTo("CleanupOldData"));
			Assert.That(queued.Dependencies, Is.Empty);
		});
	}

	[Test]
	public async Task Initialize_DownloadsOfBothStatuses_DeletesOnlyFilesUnwrittenLongerThanTheirStatusPeriod()
	{
		// Arrange
		var now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<Microsoft.Extensions.Logging.ILogger<CleanupOldDataInitializer>>(
			NullLogger<CleanupOldDataInitializer>.Instance);
		mocker.Use<TimeProvider>(new FakeTimeProvider(now));
		OptionsStubs.Options(mocker, new DownloadSettings
		{
			FileStoragePeriod = FileStoragePeriod.Month,
			PartialFileStoragePeriod = FileStoragePeriod.Day,
		});
		AppTask? queued = null;
		mocker.GetMock<IAppTaskQueue>()
			.Setup(x => x.Enqueue(It.IsAny<AppTask>(), CancellationToken.None))
			.Callback<AppTask, CancellationToken>((task, _) => queued = task)
			.ReturnsAsync(AppTaskEnqueueResult.NotEnqueued.Instance);
		var microTaskFuncs = new List<Func<ISubject<MicroTaskStep>, CancellationToken, ValueTask>>();
		mocker.GetMock<IMicroTasksFactory>()
			.Setup(x => x.CreateDelegateMicroTask(
				It.IsAny<Func<ISubject<MicroTaskStep>, CancellationToken, ValueTask>>(), It.IsAny<MicroTaskDescription>()))
			.Returns((Func<ISubject<MicroTaskStep>, CancellationToken, ValueTask> func, MicroTaskDescription _) =>
			{
				microTaskFuncs.Add(func);
				return mocker.GetMock<IAppMicroTask>().Object;
			});
		var storage = mocker.GetMock<IFileStorageService>();
		storage.Setup(x => x.GetFileInfos()).Returns([
			CreateFile("old-partial.7z", StoredFileStatus.Partial, now.AddDays(-2)),
			CreateFile("recent-partial.7z", StoredFileStatus.Partial, now.AddHours(-1)),
			CreateFile("old-finished.7z", StoredFileStatus.Complete, now.AddDays(-2)),
		]);
		storage.Setup(x => x.Delete("old-partial.7z")).Returns(IFileStorageService.DeleteResult.Deleted.Instance);
		var sut = mocker.CreateInstance<CleanupOldDataInitializer>(enablePrivate: true);
		await sut.Initialize();
		queued!.MicroTasksProvider(new MicroTasksProviderContext(mocker.Get<IMicroTasksFactory>(), mocker.Get<IServiceProvider>()));

		// Act
		await microTaskFuncs[0](new Subject<MicroTaskStep>(), CancellationToken.None);

		// Assert
		storage.Verify(x => x.Delete(It.IsAny<string>()), Times.Once);
	}

	private static StoredFileInfo CreateFile(string name, StoredFileStatus status, DateTimeOffset lastWriteAt) => new()
	{
		FileName = name,
		Size = 1,
		Status = status,
		LastWriteAt = lastWriteAt,
	};
}
