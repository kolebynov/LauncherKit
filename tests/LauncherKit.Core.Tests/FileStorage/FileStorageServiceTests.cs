using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using LauncherKit.Core.Db;
using LauncherKit.Core.Exceptions;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.Models;
using LauncherKit.Core.Tests.TestSupport;
using LauncherKit.Core.Utils;
using LauncherKit.Infrastructure;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.FileStorage;

[TestFixture]
public sealed class FileStorageServiceTests
{
	private static readonly Sha256Hash SomeHash = new(Enumerable.Range(0, 32).Select(x => (byte)x).ToArray());

	[Test]
	public async Task Allocate_SizeNotDivisibleByPartCount_PartsCoverTheWholeFile()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var temp = new TempDirectory();
		using var sut = CreateSut(context, temp.Path, out _);
		sut.GetOrCreate("payload.bin", 10, null);

		// Act
		await using var opened = sut.Allocate("payload.bin", 4);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(opened.FileParts.Sum(x => x.Size), Is.EqualTo(10));
			Assert.That(opened.FileParts.Select(x => x.Size), Is.EqualTo(new long[] { 2, 2, 2, 4 }));
		});
	}

	[Test]
	public async Task Allocate_MorePartsThanBytes_AllocatesOnePartPerByte()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var temp = new TempDirectory();
		using var sut = CreateSut(context, temp.Path, out _);
		sut.GetOrCreate("tiny.bin", 3, null);

		// Act
		await using var opened = sut.Allocate("tiny.bin", 6);

		// Assert
		Assert.That(opened.FileParts.Select(x => x.Size), Is.EqualTo(new long[] { 1, 1, 1 }));
	}

	[Test]
	public void Allocate_NotEnoughFreeSpaceForTheWholeFile_FailsBeforeReservingIt()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var temp = new TempDirectory();
		using var sut = CreateSut(context, temp.Path, out var mocker);
		sut.GetOrCreate("payload.bin", 1024, null);
		mocker.GetMock<IDiskSpaceChecker>()
			.Setup(x => x.CheckAvailableDiskSpace(It.IsAny<string>(), 1024))
			.Throws(new NotEnoughSpaceException("C:", 1024, 512));

		// Act
		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(() => sut.Allocate("payload.bin", 1), Throws.InstanceOf<NotEnoughSpaceException>());
			Assert.That(File.Exists(Path.Combine(temp.Path, "Downloads", "payload.bin.part")), Is.False);
		});
	}

	[Test]
	public async Task Allocate_StaleInProgressFileAndNotEnoughSpace_ReleasesTheStaleReservation()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var temp = new TempDirectory();
		using var sut = CreateSut(context, temp.Path, out var mocker);
		sut.GetOrCreate("payload.bin", 1024, null);
		await using (sut.Allocate("payload.bin", 1))
		{
		}

		mocker.GetMock<IDiskSpaceChecker>()
			.Setup(x => x.CheckAvailableDiskSpace(It.IsAny<string>(), 1024))
			.Throws(new NotEnoughSpaceException("C:", 1024, 512));

		// Act
		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(() => sut.Allocate("payload.bin", 1), Throws.InstanceOf<NotEnoughSpaceException>());
			Assert.That(File.Exists(Path.Combine(temp.Path, "Downloads", "payload.bin.part")), Is.False);
		});
	}

	[Test]
	public async Task GetOrCreate_InterruptedDownloadWithPreallocatedFile_ResumesInsteadOfRehashing()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var temp = new TempDirectory();
		using var sut = CreateSut(context, temp.Path, out _);
		sut.GetOrCreate("payload.bin", 8, SomeHash);
		var opened = sut.Allocate("payload.bin", 2);
		await opened.WriteAsync(0, new byte[] { 1, 2, 3, 4 }, CancellationToken.None);
		await opened.DisposeAsync();

		// Act
		await using var resumed = GetOpened(sut.GetOrCreate("payload.bin", 8, SomeHash));

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(resumed.WrittenBytes, Is.EqualTo(4));
			Assert.That(resumed.FileParts.Select(x => x.WrittenBytes), Is.EqualTo(new long[] { 4, 0 }));
			Assert.That(sut.IsFileFinished("payload.bin"), Is.False);
		});
	}

	[Test]
	public async Task GetOrCreate_FileCopiedIntoDownloadsDirectory_RequestsRehashAndDropsInProgressState()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var temp = new TempDirectory();
		using var sut = CreateSut(context, temp.Path, out _);
		sut.GetOrCreate("payload.bin", 8, SomeHash);
		var opened = sut.Allocate("payload.bin", 2);
		await opened.WriteAsync(0, new byte[] { 1, 2, 3, 4 }, CancellationToken.None);
		await opened.DisposeAsync();
		await File.WriteAllBytesAsync(Path.Combine(temp.Path, "Downloads", "payload.bin"), new byte[8]);

		// Act
		var result = sut.GetOrCreate("payload.bin", 8, SomeHash);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(IsRehashRequested(result), Is.True);
			Assert.That(File.Exists(Path.Combine(temp.Path, "Downloads", "payload.bin.part")), Is.False);
			Assert.That(context.Count("FileParts"), Is.Zero);
		});
	}

	[Test]
	public async Task Complete_FullyDownloadedFile_PublishesFileUnderFinalName()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var temp = new TempDirectory();
		using var sut = CreateSut(context, temp.Path, out _);
		var payload = new byte[] { 1, 2, 3, 4 };
		sut.GetOrCreate("payload.bin", payload.Length, SomeHash);
		var opened = sut.Allocate("payload.bin", 2);
		await opened.WriteAsync(0, payload.AsMemory(0, 2), CancellationToken.None);
		await opened.WriteAsync(1, payload.AsMemory(2, 2), CancellationToken.None);
		await opened.DisposeAsync();

		// Act
		sut.Complete("payload.bin");

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(File.ReadAllBytes(Path.Combine(temp.Path, "Downloads", "payload.bin")), Is.EqualTo(payload));
			Assert.That(File.Exists(Path.Combine(temp.Path, "Downloads", "payload.bin.part")), Is.False);
			Assert.That(sut.IsFileFinished("payload.bin"), Is.True);
			Assert.That(context.Count("FileParts"), Is.Zero);
		});
	}

	[Test]
	public async Task Complete_IncompleteDownload_ThrowsAndKeepsInProgressFile()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var temp = new TempDirectory();
		using var sut = CreateSut(context, temp.Path, out _);
		sut.GetOrCreate("payload.bin", 8, null);
		var opened = sut.Allocate("payload.bin", 2);
		await opened.WriteAsync(0, new byte[] { 1, 2, 3, 4 }, CancellationToken.None);
		await opened.DisposeAsync();

		// Act
		// Assert
		Assert.That(() => sut.Complete("payload.bin"),
			Throws.InvalidOperationException.With.Message.Contains("not finished"));
		Assert.Multiple(() =>
		{
			Assert.That(File.Exists(Path.Combine(temp.Path, "Downloads", "payload.bin.part")), Is.True);
			Assert.That(sut.IsFileFinished("payload.bin"), Is.False);
		});
	}

	[TestCase(true, Description = "Adopted external file")]
	[TestCase(false, Description = "Rejected external file")]
	public async Task Rehash_ExternalFile_DiscardsTheInterruptedDownloadOfTheSameFile(bool isHashMatching)
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var temp = new TempDirectory();
		using var sut = CreateSut(context, temp.Path, out var mocker);
		sut.GetOrCreate("payload.bin", 4, SomeHash);
		var opened = sut.Allocate("payload.bin", 1);
		await opened.WriteAsync(0, new byte[] { 7, 7 }, CancellationToken.None);
		await opened.DisposeAsync();
		var finalPath = Path.Combine(temp.Path, "Downloads", "payload.bin");
		await File.WriteAllBytesAsync(finalPath, new byte[] { 1, 2, 3, 4 });
		mocker.GetMock<IHashCalculator>()
			.Setup(x => x.HashFile(finalPath, null, CancellationToken.None))
			.ReturnsAsync(isHashMatching ? SomeHash : new Sha256Hash(new byte[32]));

		// Act
		var result = await sut.Rehash("payload.bin", null, CancellationToken.None);

		// Assert
		var isAdopted = result is IFileStorageService.RehashResult.Finished;
		if (result is IFileStorageService.RehashResult.Finished(var adoptedFile))
		{
			adoptedFile.Dispose();
		}

		Assert.Multiple(() =>
		{
			Assert.That(isAdopted, Is.EqualTo(isHashMatching));
			Assert.That(sut.IsFileFinished("payload.bin"), Is.EqualTo(isHashMatching));
			Assert.That(File.Exists(finalPath), Is.EqualTo(isHashMatching));
			Assert.That(File.Exists(finalPath + ".part"), Is.False);
			Assert.That(context.Count("FileParts"), Is.Zero);
		});
	}

	[Test]
	public async Task GetOrCreate_InProgressDownloadWithDifferentSize_ThrowsWithoutChangingRecord()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var temp = new TempDirectory();
		using var sut = CreateSut(context, temp.Path, out _);
		sut.GetOrCreate("payload.bin", 3, null);
		var opened = sut.Allocate("payload.bin", 1);
		await opened.DisposeAsync();

		// Act
		// Assert
		Assert.That(() => sut.GetOrCreate("payload.bin", 4, null),
			Throws.InvalidOperationException.With.Message.Contains("file size does not match"));
		Assert.That(context.Scalar("""SELECT "Size" FROM "Files" """), Is.EqualTo(3));
	}

	[Test]
	public async Task GetOrCreate_AlreadyPublishedFile_ReturnsFinishedFileForReading()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var temp = new TempDirectory();
		using var sut = CreateSut(context, temp.Path, out _);
		var payload = new byte[] { 4, 5, 6 };
		sut.GetOrCreate("payload.bin", payload.Length, null);
		var opened = sut.Allocate("payload.bin", 1);
		await opened.WriteAsync(0, payload, CancellationToken.None);
		await opened.DisposeAsync();
		sut.Complete("payload.bin");

		// Act
		await using var finished = GetFinished(sut.GetOrCreate("payload.bin", payload.Length, null));
		var bytes = new byte[payload.Length];
		var read = await finished.ReadAsync(bytes);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(read, Is.EqualTo(payload.Length));
			Assert.That(bytes, Is.EqualTo(payload));
			Assert.That(sut.IsFileFinished("payload.bin"), Is.True);
		});
	}

	[Test]
	public async Task Delete_InProgressDownload_RemovesInProgressFileAndParts()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var temp = new TempDirectory();
		using var sut = CreateSut(context, temp.Path, out _);
		sut.GetOrCreate("payload.bin", 8, null);
		var opened = sut.Allocate("payload.bin", 2);
		await opened.WriteAsync(0, new byte[] { 1, 2, 3, 4 }, CancellationToken.None);
		await opened.DisposeAsync();

		// Act
		var result = sut.Delete("payload.bin");

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result is IFileStorageService.DeleteResult.Deleted, Is.True);
			Assert.That(File.Exists(Path.Combine(temp.Path, "Downloads", "payload.bin.part")), Is.False);
			Assert.That(context.Count("FileParts"), Is.Zero);
			Assert.That(context.Count("Files"), Is.Zero);
		});
	}

	[Test]
	public async Task Delete_InProgressFileOpenedByDownload_ReturnsFileInUseAndKeepsFile()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var temp = new TempDirectory();
		using var sut = CreateSut(context, temp.Path, out _);
		sut.GetOrCreate("payload.bin", 8, null);
		await using var opened = sut.Allocate("payload.bin", 2);
		var inProgressPath = Path.Combine(temp.Path, "Downloads", "payload.bin.part");

		// Act
		var result = sut.Delete("payload.bin");

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result is IFileStorageService.DeleteResult.FileInUse(var path) ? path : null, Is.EqualTo(inProgressPath));
			Assert.That(File.Exists(inProgressPath), Is.True);
			Assert.That(sut.GetFileInfos().Select(x => x.FileName), Is.EqualTo(new[] { "payload.bin" }));
		});
	}

	[Test]
	public async Task GetFileInfos_PartialDownload_ReportsPartialStatusAndLatestPartWrite()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var temp = new TempDirectory();
		using var sut = CreateSut(context, temp.Path, out var mocker);
		var timeProvider = (FakeTimeProvider)mocker.Get<TimeProvider>();
		sut.GetOrCreate("payload.bin", 8, null);
		var opened = sut.Allocate("payload.bin", 2);
		timeProvider.Advance(TimeSpan.FromHours(1));
		await opened.WriteAsync(1, new byte[] { 1, 2 }, CancellationToken.None);
		await opened.DisposeAsync();
		var lastWriteAt = timeProvider.GetUtcNow();
		timeProvider.Advance(TimeSpan.FromHours(1));

		// Act
		var fileInfo = sut.GetFileInfos().Single();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(fileInfo.Status, Is.EqualTo(StoredFileStatus.Partial));
			Assert.That(fileInfo.LastWriteAt, Is.EqualTo(lastWriteAt));
			Assert.That(fileInfo.Size, Is.EqualTo(8));
		});
	}

	[Test]
	public async Task GetFileInfos_FinishedDownload_ReportsCompleteStatusAndFinishTime()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var temp = new TempDirectory();
		using var sut = CreateSut(context, temp.Path, out var mocker);
		var timeProvider = (FakeTimeProvider)mocker.Get<TimeProvider>();
		sut.GetOrCreate("payload.bin", 2, null);
		var opened = sut.Allocate("payload.bin", 1);
		await opened.WriteAsync(0, new byte[] { 1, 2 }, CancellationToken.None);
		await opened.DisposeAsync();
		timeProvider.Advance(TimeSpan.FromHours(1));
		sut.Complete("payload.bin");
		var finishedAt = timeProvider.GetUtcNow();
		timeProvider.Advance(TimeSpan.FromHours(1));

		// Act
		var fileInfo = sut.GetFileInfos().Single();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(fileInfo.Status, Is.EqualTo(StoredFileStatus.Complete));
			Assert.That(fileInfo.LastWriteAt, Is.EqualTo(finishedAt));
		});
	}

	[Test]
	public async Task MigrateLegacyPartialDownloads_PartialFileAtFinalPath_ResumesFromMigratedProgress()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var temp = new TempDirectory();
		using var sut = CreateSut(context, temp.Path, out _);
		sut.GetOrCreate("payload.bin", 8, null);
		var legacyPath = Path.Combine(temp.Path, "Downloads", "payload.bin");
		await File.WriteAllBytesAsync(legacyPath, new byte[] { 1, 2, 3, 4 });
		InsertFilePart(context, "payload.bin", size: 8, writtenBytes: 4);

		// Act
		sut.MigrateLegacyPartialDownloads();
		await using var resumed = GetOpened(sut.GetOrCreate("payload.bin", 8, null));

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(File.Exists(legacyPath), Is.False);
			Assert.That(resumed.WrittenBytes, Is.EqualTo(4));
		});
	}

	[Test]
	public async Task MigrateLegacyPartialDownloads_ExternallyPlacedFile_KeepsFileAtFinalPath()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var temp = new TempDirectory();
		using var sut = CreateSut(context, temp.Path, out _);
		sut.GetOrCreate("payload.bin", 8, SomeHash);
		var externalPath = Path.Combine(temp.Path, "Downloads", "payload.bin");
		await File.WriteAllBytesAsync(externalPath, new byte[8]);
		InsertFilePart(context, "payload.bin", size: 8, writtenBytes: 4);

		// Act
		sut.MigrateLegacyPartialDownloads();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(File.Exists(externalPath), Is.True);
			Assert.That(IsRehashRequested(sut.GetOrCreate("payload.bin", 8, SomeHash)), Is.True);
		});
	}

	private static FileStream GetFinished(IFileStorageService.GetOrCreateResult result) =>
		result switch
		{
			IFileStorageService.GetOrCreateResult.Existing(var descriptor) => descriptor switch
			{
				FileDescriptor.Opened => throw new AssertionException("Expected a finished file"),
				FileDescriptor.Finished(var finished) => finished,
			},
			IFileStorageService.GetOrCreateResult.NeedRehash => throw new AssertionException("Expected a file descriptor"),
			IFileStorageService.GetOrCreateResult.NeedAllocate => throw new AssertionException("Expected a file descriptor"),
		};

	internal static FileStorageService CreateSut(
		GlobalDbContext context, string appDataDirectory, out AutoMocker mocker)
	{
		mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use(context);
		mocker.Use<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
		mocker.Use<Microsoft.Extensions.Logging.ILogger<FileStorageService>>(NullLogger<FileStorageService>.Instance);
		OptionsStubs.Monitor(mocker, new AppSettings { AppDataDirectory = appDataDirectory });
		mocker.GetMock<IDiskSpaceChecker>()
			.Setup(x => x.CheckAvailableDiskSpace(It.IsAny<string>(), It.IsAny<long>()));
		var fileSystem = mocker.GetMock<IFileSystemUtils>();
		fileSystem.Setup(x => x.EnsureDirectoryExists(It.IsAny<string>(), It.IsAny<bool>()))
			.Callback((string directory, bool _) => Directory.CreateDirectory(directory));
		fileSystem.Setup(x => x.GetFileSize(It.IsAny<string>()))
			.Returns((string path) => File.Exists(path) ? new System.IO.FileInfo(path).Length : null);
		fileSystem.Setup(x => x.MoveFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
			.Callback((string source, string destination, bool overwrite) =>
				File.Move(source, destination, overwrite));
		fileSystem.Setup(x => x.SafeDeleteFile(It.IsAny<string>()))
			.Returns((string path) =>
			{
				if (!File.Exists(path))
				{
					return FileSystemUtils.SafeDeleteResult.NotExist.Instance;
				}

				try
				{
					File.Delete(path);
					return FileSystemUtils.SafeDeleteResult.Success.Instance;
				}
				catch (IOException e)
				{
					return new FileSystemUtils.SafeDeleteResult.Error(e);
				}
			});
		return mocker.CreateInstance<FileStorageService>();
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

	private static bool IsRehashRequested(IFileStorageService.GetOrCreateResult result) =>
		result is IFileStorageService.GetOrCreateResult.NeedRehash;

	private static void InsertFilePart(GlobalDbContext context, string fileName, long size, long writtenBytes) =>
		context.Execute(
			"""
			INSERT INTO "FileParts" ("FileName", "Index", "Size", "WrittenBytes", "LastWriteAt")
			VALUES ($fileName, 0, $size, $writtenBytes, 0)
			""",
			("$fileName", fileName), ("$size", size), ("$writtenBytes", writtenBytes));
}
