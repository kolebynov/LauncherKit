using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using LauncherKit.Core.Db;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services;
using LauncherKit.Core.Tests.FileStorage;
using LauncherKit.Core.Tests.TestSupport;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.Db;

/// <summary>
/// <c>Fixtures/Db/launcher.db</c> was written by the Linq2DB data access of earlier launchers: every global
/// migration, the key values <c>channel = beta</c> and <c>empty = NULL</c>, the finished <c>finished.bin</c>
/// (3 bytes, hash 00..1F) and <c>partial.bin</c> (10 bytes in two parts of 5, 2 bytes written to the first),
/// all timestamps 2026-09-08 12:00 UTC. Only the file paths were rewritten afterwards to
/// <c>C:\stb\Downloads\</c>; each test points them at its own downloads directory.
/// </summary>
[TestFixture]
public sealed class Linq2DbLauncherDatabaseTests
{
	private static readonly Sha256Hash FinishedFileHash = new(Enumerable.Range(0, 32).Select(x => (byte)x).ToArray());
	private static readonly DateTimeOffset WrittenAt = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

	[Test]
	public void Migrate_Linq2DbDatabase_AppliesNothingMore()
	{
		// Arrange
		using var database = CopyFixture();
		using var context = database.CreateGlobalContext();
		var sut = new DbMigrator<GlobalDbContext>(context, new GlobalMigrationProvider(),
			NullLogger<DbMigrator<GlobalDbContext>>.Instance, TimeProvider.System);

		// Act
		sut.Migrate();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(context.Count("__Migrations"), Is.EqualTo(new GlobalMigrationProvider().GetMigrations().Count));
			Assert.That(context.Scalar("""SELECT MAX("AppliedAt") FROM "__Migrations" """),
				Is.EqualTo(new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds()));
		});
	}

	[Test]
	public void GlobalKeyValueStorage_Linq2DbDatabase_ReadsAndOverwritesStoredValues()
	{
		// Arrange
		using var database = CopyFixture();
		using var context = database.CreateGlobalContext();
		using var provider = new ServiceCollection().AddSingleton(context).BuildServiceProvider();
		using var scope = new GlobalScope(provider);
		var sut = new GlobalKeyValueStorage(scope);

		// Act
		var channel = sut.Get("channel");
		var hasEmpty = sut.HasKey("empty");
		var empty = sut.Get("empty");
		sut.Set("channel", "stable");

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(channel, Is.EqualTo("beta"));
			Assert.That(hasEmpty, Is.True);
			Assert.That(empty, Is.Null);
			Assert.That(sut.Get("channel"), Is.EqualTo("stable"));
			Assert.That(context.Count("KeyValues"), Is.EqualTo(2));
		});
	}

	[Test]
	public async Task GetOrCreate_Linq2DbFinishedFile_ReturnsTheStoredFile()
	{
		// Arrange
		using var database = CopyFixture();
		using var context = database.CreateGlobalContext();
		using var temp = new TempDirectory();
		var downloadsDirectory = PointFilesAt(context, temp.Path);
		await File.WriteAllBytesAsync(Path.Combine(downloadsDirectory, "finished.bin"), new byte[] { 1, 2, 3 });
		using var sut = FileStorageServiceTests.CreateSut(context, temp.Path, out _);

		// Act
		var result = sut.GetOrCreate("finished.bin", 3, FinishedFileHash);

		// Assert
		await using var stream = result is IFileStorageService.GetOrCreateResult.Existing(FileDescriptor.Finished(var finished))
			? finished
			: throw new AssertionException("Expected a finished file");
		var stored = sut.GetFileInfos().Single(x => x.FileName == "finished.bin");
		Assert.Multiple(() =>
		{
			Assert.That(stream.Length, Is.EqualTo(3));
			Assert.That(stored.Status, Is.EqualTo(StoredFileStatus.Complete));
			Assert.That(stored.LastWriteAt, Is.EqualTo(WrittenAt));
		});
	}

	[Test]
	public async Task GetOrCreate_Linq2DbPartialFile_ResumesAndCompletesTheDownload()
	{
		// Arrange
		using var database = CopyFixture();
		using var context = database.CreateGlobalContext();
		using var temp = new TempDirectory();
		var downloadsDirectory = PointFilesAt(context, temp.Path);
		await File.WriteAllBytesAsync(Path.Combine(downloadsDirectory, "partial.bin.part"), new byte[10]);
		using var sut = FileStorageServiceTests.CreateSut(context, temp.Path, out _);

		// Act
		var result = sut.GetOrCreate("partial.bin", 10, null);
		long resumedBytes;
		long[] partSizes;
		await using (var opened = result is IFileStorageService.GetOrCreateResult.Existing(FileDescriptor.Opened(var file))
			? file
			: throw new AssertionException("Expected an opened file"))
		{
			resumedBytes = opened.WrittenBytes;
			partSizes = [.. opened.FileParts.Select(x => x.Size)];
			await opened.WriteAsync(0, new byte[3], CancellationToken.None);
			await opened.WriteAsync(1, new byte[5], CancellationToken.None);
		}

		sut.Complete("partial.bin");

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(resumedBytes, Is.EqualTo(2));
			Assert.That(partSizes, Is.EqualTo(new long[] { 5, 5 }));
			Assert.That(sut.IsFileFinished("partial.bin"), Is.True);
			Assert.That(File.Exists(Path.Combine(downloadsDirectory, "partial.bin")), Is.True);
			Assert.That(context.Count("FileParts"), Is.Zero);
		});
	}

	private static SqliteDatabase CopyFixture()
	{
		var database = new SqliteDatabase();
		File.Copy(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Db", "launcher.db"),
			database.GlobalDbPath);
		return database;
	}

	/// <returns>The downloads directory of <paramref name="appDataDirectory"/>, where the files now point.</returns>
	private static string PointFilesAt(GlobalDbContext context, string appDataDirectory)
	{
		var downloadsDirectory = Directory.CreateDirectory(Path.Combine(appDataDirectory, "Downloads")).FullName;
		context.Execute("""UPDATE "Files" SET "Path" = $directory || "FileName" """,
			("$directory", downloadsDirectory + Path.DirectorySeparatorChar));
		return downloadsDirectory;
	}
}
