using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using R3;
using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.Core.Exceptions;
using LauncherKit.Core.FileSources;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Tests.FileStorage;
using LauncherKit.Core.Tests.TestSupport;
using LauncherKit.Infrastructure;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.AppTasks.MicroTasks;

[TestFixture]
public sealed class DownloadFileMicroTaskTests
{
	private static readonly Uri FileUri = new("https://files.test/archive.bin");

	[Test]
	public async Task Execute_TargetAlreadyFinished_DoesNotCreateSourceOrStartDownload()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<DownloadFileSource>(new DownloadFileSource.Http(FileUri, "archive.bin"));
		mocker.Use<Microsoft.Extensions.Logging.ILogger<DownloadFileMicroTask>>(
			NullLogger<DownloadFileMicroTask>.Instance);
		mocker.Use(TimeProvider.System);
		mocker.GetMock<IFileStorageService>()
			.Setup(x => x.IsFileFinished("archive.bin"))
			.Returns(true);
		var sut = mocker.CreateInstance<DownloadFileMicroTask>(enablePrivate: true);
		using var subject = new Subject<MicroTaskStep>();

		// Act
		await sut.Execute(subject, CancellationToken.None);

		// Assert
		mocker.GetMock<IFileSourceDownloader>().VerifyNoOtherCalls();
		mocker.GetMock<IHttpFileSourceFactory>().VerifyNoOtherCalls();
	}

	[Test]
	public async Task Execute_DownloadFinishes_PublishesFileThroughStorageCompletion()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var temp = new TempDirectory();
		using var storage = FileStorageServiceTests.CreateSut(context, temp.Path, out _);
		storage.GetOrCreate("archive.bin", 1024, null);
		await using var destination = storage.Allocate("archive.bin", 1);
		var mocker = CreateMocker(1024, isOffsetSupported: true, destination, out var storageMock);
		mocker.GetMock<IFileSourceDownloader>()
			.Setup(x => x.Download(It.IsAny<IFileSource>(), destination,
				It.IsAny<Func<DownloadFileProgress, ValueTask>?>(), It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);
		var sut = mocker.CreateInstance<DownloadFileMicroTask>(enablePrivate: true);
		using var subject = new Subject<MicroTaskStep>();

		// Act
		await sut.Execute(subject, CancellationToken.None);

		// Assert
		storageMock.Verify(
			x => x.Complete("archive.bin"),
			Times.Once);
	}

	[TestCase(1024L, 1, Description = "Small file is downloaded as a single part")]
	[TestCase(300L * 1024 * 1024, 2, Description = "One part per 128 MiB")]
	[TestCase(100L * 1024 * 1024 * 1024, 6, Description = "Part count is capped")]
	public async Task Execute_RangesSupported_AllocatesOnePartPer128Mib(long fileSize, int expectedPartCount)
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var temp = new TempDirectory();
		using var storage = FileStorageServiceTests.CreateSut(context, temp.Path, out _);
		storage.GetOrCreate("archive.bin", 1024, null);
		await using var destination = storage.Allocate("archive.bin", 1);
		var mocker = CreateMocker(fileSize, isOffsetSupported: true, destination, out var storageMock);
		mocker.GetMock<IFileSourceDownloader>()
			.Setup(x => x.Download(It.IsAny<IFileSource>(), destination,
				It.IsAny<Func<DownloadFileProgress, ValueTask>?>(), It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);
		var sut = mocker.CreateInstance<DownloadFileMicroTask>(enablePrivate: true);
		using var subject = new Subject<MicroTaskStep>();

		// Act
		await sut.Execute(subject, CancellationToken.None);

		// Assert
		storageMock.Verify(x => x.Allocate("archive.bin", expectedPartCount), Times.Once);
	}

	[Test]
	public async Task Execute_RangesNotSupported_AllocatesSinglePart()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var temp = new TempDirectory();
		using var storage = FileStorageServiceTests.CreateSut(context, temp.Path, out _);
		storage.GetOrCreate("archive.bin", 1024, null);
		await using var destination = storage.Allocate("archive.bin", 1);
		var mocker = CreateMocker(10L * 1024 * 1024 * 1024, isOffsetSupported: false, destination, out var storageMock);
		mocker.GetMock<IFileSourceDownloader>()
			.Setup(x => x.Download(It.IsAny<IFileSource>(), destination,
				It.IsAny<Func<DownloadFileProgress, ValueTask>?>(), It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);
		var sut = mocker.CreateInstance<DownloadFileMicroTask>(enablePrivate: true);
		using var subject = new Subject<MicroTaskStep>();

		// Act
		await sut.Execute(subject, CancellationToken.None);

		// Assert
		storageMock.Verify(x => x.Allocate("archive.bin", 1), Times.Once);
	}

	[Test]
	public async Task Execute_RangesNotSupportedForInterruptedDownload_RestartsDownloadFromScratch()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var temp = new TempDirectory();
		using var storage = FileStorageServiceTests.CreateSut(context, temp.Path, out _);
		storage.GetOrCreate("archive.bin", 1024, null);
		var allocated = storage.Allocate("archive.bin", 1);
		await allocated.WriteAsync(0, new byte[] { 1, 2, 3, 4 }, CancellationToken.None);
		await allocated.DisposeAsync();
		var interrupted = GetOpened(storage.GetOrCreate("archive.bin", 1024, null));
		var mocker = CreateMocker(1024, isOffsetSupported: false, interrupted, out var storageMock);
		storageMock.Setup(x => x.GetOrCreate("archive.bin", 1024, null))
			.Returns(new IFileStorageService.GetOrCreateResult.Existing(new FileDescriptor.Opened(interrupted)));
		storageMock.Setup(x => x.Allocate("archive.bin", 1)).Returns(() => storage.Allocate("archive.bin", 1));
		OpenedFile? downloadDestination = null;
		mocker.GetMock<IFileSourceDownloader>()
			.Setup(x => x.Download(It.IsAny<IFileSource>(), It.IsAny<OpenedFile>(),
				It.IsAny<Func<DownloadFileProgress, ValueTask>?>(), It.IsAny<CancellationToken>()))
			.Callback((IFileSource _, OpenedFile destination, Func<DownloadFileProgress, ValueTask>? _,
				CancellationToken _) => downloadDestination = destination)
			.Returns(Task.CompletedTask);
		var sut = mocker.CreateInstance<DownloadFileMicroTask>(enablePrivate: true);
		using var subject = new Subject<MicroTaskStep>();

		// Act
		await sut.Execute(subject, CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(downloadDestination, Is.Not.Null.And.Not.SameAs(interrupted));
			Assert.That(downloadDestination!.WrittenBytes, Is.Zero);
		});
	}

	[Test]
	public async Task Execute_NoDataForStallTimeout_ThrowsDownloadStalled()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var temp = new TempDirectory();
		using var storage = FileStorageServiceTests.CreateSut(context, temp.Path, out _);
		storage.GetOrCreate("archive.bin", 1024, null);
		await using var destination = storage.Allocate("archive.bin", 1);
		var timeProvider = new FakeTimeProvider();
		var mocker = CreateMocker(1024, isOffsetSupported: true, destination, out _, timeProvider);
		var downloadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		mocker.GetMock<IFileSourceDownloader>()
			.Setup(x => x.Download(It.IsAny<IFileSource>(), destination,
				It.IsAny<Func<DownloadFileProgress, ValueTask>?>(), It.IsAny<CancellationToken>()))
			.Returns((IFileSource _, OpenedFile _, Func<DownloadFileProgress, ValueTask>? _, CancellationToken ct) =>
			{
				downloadStarted.SetResult();
				return Task.Delay(Timeout.InfiniteTimeSpan, ct);
			});
		var sut = mocker.CreateInstance<DownloadFileMicroTask>(enablePrivate: true);
		using var subject = new Subject<MicroTaskStep>();
		var execution = sut.Execute(subject, CancellationToken.None).AsTask();
		await downloadStarted.Task;

		// Act
		timeProvider.Advance(DownloadFileMicroTask.StallTimeout);

		// Assert
		Assert.That(async () => await execution, Throws.InstanceOf<DownloadStalledException>());
	}

	[Test]
	public async Task Execute_DataKeepsArriving_DoesNotStallAfterStallTimeout()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var temp = new TempDirectory();
		using var storage = FileStorageServiceTests.CreateSut(context, temp.Path, out _);
		storage.GetOrCreate("archive.bin", 1024, null);
		await using var destination = storage.Allocate("archive.bin", 1);
		var timeProvider = new FakeTimeProvider();
		var mocker = CreateMocker(1024, isOffsetSupported: true, destination, out var storageMock, timeProvider);
		var halfTimeout = DownloadFileMicroTask.StallTimeout / 2 + TimeSpan.FromSeconds(1);
		mocker.GetMock<IFileSourceDownloader>()
			.Setup(x => x.Download(It.IsAny<IFileSource>(), destination,
				It.IsAny<Func<DownloadFileProgress, ValueTask>?>(), It.IsAny<CancellationToken>()))
			.Returns(async (IFileSource _, OpenedFile _, Func<DownloadFileProgress, ValueTask>? progress,
				CancellationToken ct) =>
			{
				// Two gaps shorter than the timeout that add up to more than it.
				timeProvider.Advance(halfTimeout);
				await progress!(new DownloadFileProgress(512, 1024, 0));
				timeProvider.Advance(halfTimeout);
				ct.ThrowIfCancellationRequested();
			});
		var sut = mocker.CreateInstance<DownloadFileMicroTask>(enablePrivate: true);
		using var subject = new Subject<MicroTaskStep>();

		// Act
		await sut.Execute(subject, CancellationToken.None);

		// Assert
		storageMock.Verify(x => x.Complete("archive.bin"), Times.Once);
	}

	[Test]
	public async Task Execute_ResumedBeforePausedAttemptUnwinds_RestartsDownloadInsteadOfFailing()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var temp = new TempDirectory();
		using var storage = FileStorageServiceTests.CreateSut(context, temp.Path, out _);
		storage.GetOrCreate("archive.bin", 1024, null);
		await using var destination = storage.Allocate("archive.bin", 1);
		var mocker = CreateMocker(1024, isOffsetSupported: true, destination, out var storageMock);
		using var subject = new Subject<MicroTaskStep>();
		IDownloadProgress? shownProgress = null;
		using var subscription = subject.Subscribe(step =>
		{
			if (step is MicroTaskStep.Progress(var progress))
			{
				shownProgress = (IDownloadProgress)progress;
			}
		});
		var attempts = 0;
		mocker.GetMock<IFileSourceDownloader>()
			.Setup(x => x.Download(It.IsAny<IFileSource>(), destination,
				It.IsAny<Func<DownloadFileProgress, ValueTask>?>(), It.IsAny<CancellationToken>()))
			.Returns(async (IFileSource _, OpenedFile _, Func<DownloadFileProgress, ValueTask>? progress,
				CancellationToken ct) =>
			{
				if (++attempts > 1)
				{
					return;
				}

				await progress!(new DownloadFileProgress(512, 1024, 0));
				// The user resumes before the cancelled transfer has unwound.
				await shownProgress!.Pause();
				await shownProgress.Resume();
				ct.ThrowIfCancellationRequested();
			});
		var sut = mocker.CreateInstance<DownloadFileMicroTask>(enablePrivate: true);

		// Act
		await sut.Execute(subject, CancellationToken.None);

		// Assert
		Assert.That(attempts, Is.EqualTo(2));
		storageMock.Verify(x => x.Complete("archive.bin"), Times.Once);
	}

	private static OpenedFile GetOpened(IFileStorageService.GetOrCreateResult result) =>
		result switch
		{
			IFileStorageService.GetOrCreateResult.Existing(var descriptor) => descriptor switch
			{
				FileDescriptor.Opened(var opened) => opened,
				FileDescriptor.Finished => throw new AssertionException("Expected an opened file"),
			},
			IFileStorageService.GetOrCreateResult.NeedRehash => throw new AssertionException("Expected a file descriptor"),
			IFileStorageService.GetOrCreateResult.NeedAllocate => throw new AssertionException("Expected a file descriptor"),
		};

	private static AutoMocker CreateMocker(
		long fileSize, bool isOffsetSupported, OpenedFile destination, out Mock<IFileStorageService> storageMock,
		TimeProvider? timeProvider = null)
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<DownloadFileSource>(new DownloadFileSource.Http(FileUri, "archive.bin"));
		mocker.Use<Microsoft.Extensions.Logging.ILogger<DownloadFileMicroTask>>(
			NullLogger<DownloadFileMicroTask>.Instance);
		mocker.Use(timeProvider ?? TimeProvider.System);

		var source = mocker.GetMock<IFileSource>();
		source.Setup(x => x.GetFileSize(It.IsAny<CancellationToken>())).ReturnsAsync(fileSize);
		source.Setup(x => x.GetHash(It.IsAny<CancellationToken>())).ReturnsAsync((Sha256Hash?)null);
		source.Setup(x => x.IsOffsetSupported(It.IsAny<CancellationToken>())).ReturnsAsync(isOffsetSupported);
		mocker.GetMock<IHttpFileSourceFactory>().Setup(x => x.Create(FileUri, null)).Returns(source.Object);

		storageMock = mocker.GetMock<IFileStorageService>();
		storageMock.Setup(x => x.IsFileFinished("archive.bin")).Returns(false);
		storageMock.Setup(x => x.GetOrCreate("archive.bin", fileSize, null))
			.Returns(IFileStorageService.GetOrCreateResult.NeedAllocate.Instance);
		storageMock.Setup(x => x.Allocate("archive.bin", It.IsAny<int>())).Returns(destination);
		storageMock.Setup(x => x.Complete("archive.bin"));
		return mocker;
	}
}
