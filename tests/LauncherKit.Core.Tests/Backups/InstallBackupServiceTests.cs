using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Versioning;
using LauncherKit.Core.Backups;
using LauncherKit.Core.Db;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Tests.TestSupport;
using LauncherKit.Infrastructure;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.Backups;

[TestFixture]
public sealed class InstallBackupServiceTests
{
	private static readonly InstallActionBackup Backup =
		InstallBackupId.ForModVersion("Mod", NuGetVersion.Parse("1.0.0")).ForAction(1, 2);

	[Test]
	public async Task Restore_AfterDeleteWithBackup_RestoresDeletedFileAndDirectoryAndRemovesBackup()
	{
		// Arrange
		using var skyrim = new TempDirectory();
		using var database = new SqliteDatabase();
		using var dbContext = database.CreateMigratedSkyrimLocalContext();
		var sut = CreateSut(LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict), skyrim.Path, dbContext);
		skyrim.CreateFile(@"Data\old.esp", "plugin");
		skyrim.CreateFile(@"Cache\a\shader.bin", "shader");
		skyrim.CreateDirectory(@"Cache\empty");

		// Act
		var fileResult = sut.DeleteWithBackup(Backup, new SkyrimRelativePath(@"Data\old.esp"), CancellationToken.None);
		var directoryResult = sut.DeleteWithBackup(Backup, new SkyrimRelativePath("Cache"), CancellationToken.None);
		var missingResult = sut.DeleteWithBackup(Backup, new SkyrimRelativePath("missing"), CancellationToken.None);
		var deleted = !File.Exists(skyrim.Combine(@"Data\old.esp")) && !Directory.Exists(skyrim.Combine("Cache"));
		var restored = await sut.Restore(Backup, CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(fileResult.Value, Is.TypeOf<FileSystemUtils.SafeDeleteResult.Success>());
			Assert.That(directoryResult.Value, Is.TypeOf<FileSystemUtils.SafeDeleteResult.Success>());
			Assert.That(missingResult.Value, Is.TypeOf<FileSystemUtils.SafeDeleteResult.NotExist>());
			Assert.That(deleted, Is.True);
			Assert.That(restored, Is.True);
			Assert.That(File.ReadAllText(skyrim.Combine(@"Data\old.esp")), Is.EqualTo("plugin"));
			Assert.That(File.ReadAllText(skyrim.Combine(@"Cache\a\shader.bin")), Is.EqualTo("shader"));
			Assert.That(Directory.Exists(skyrim.Combine(@"Cache\empty")), Is.True);
			Assert.That(Directory.Exists(skyrim.Combine(@".ExampleLauncher\Backups\Mods\Mod\1.0.0\1.2")), Is.False);
		});
	}

	[Test]
	public async Task DeleteWithBackup_FileAlreadyBackedUp_DeletesFileAndKeepsOriginalBackup()
	{
		// Arrange
		using var skyrim = new TempDirectory();
		using var database = new SqliteDatabase();
		using var dbContext = database.CreateMigratedSkyrimLocalContext();
		var sut = CreateSut(LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict), skyrim.Path, dbContext);
		skyrim.CreateFile("file.txt", "original");
		sut.DeleteWithBackup(Backup, new SkyrimRelativePath("file.txt"), CancellationToken.None);
		skyrim.CreateFile("file.txt", "recreated");

		// Act
		var result = sut.DeleteWithBackup(Backup, new SkyrimRelativePath("file.txt"), CancellationToken.None);
		await sut.Restore(Backup, CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result.Value, Is.TypeOf<FileSystemUtils.SafeDeleteResult.Success>());
			Assert.That(File.ReadAllText(skyrim.Combine("file.txt")), Is.EqualTo("original"));
		});
	}

	[TestCase("")]
	[TestCase(".ExampleLauncher")]
	[TestCase(@".ExampleLauncher\Backups\Mods")]
	public void DeleteWithBackup_PathContainingOrInsideBackups_Throws(string path)
	{
		// Arrange
		using var skyrim = new TempDirectory();
		using var database = new SqliteDatabase();
		using var dbContext = database.CreateMigratedSkyrimLocalContext();
		var sut = CreateSut(LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict), skyrim.Path, dbContext);
		skyrim.CreateDirectory(@".ExampleLauncher\Backups\Mods");

		// Act
		void Act() => sut.DeleteWithBackup(Backup, new SkyrimRelativePath(path), CancellationToken.None);

		// Assert
		Assert.That(Act, Throws.ArgumentException);
	}

	[Test]
	public async Task Restore_AfterExtraction_RestoresOverwrittenFilesAndDeletesCreatedOnes()
	{
		// Arrange
		using var skyrim = new TempDirectory();
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		SetupArchive(mocker,
			new ArchiveEntry("enb.dll", 3, false),
			new ArchiveEntry("Shaders", 0, true),
			new ArchiveEntry(@"Shaders\Deep", 0, true),
			new ArchiveEntry(@"Shaders\Deep\fx.hlsl", 2, false),
			new ArchiveEntry(@"Plugins\new.dll", 1, false));
		using var database = new SqliteDatabase();
		using var dbContext = database.CreateMigratedSkyrimLocalContext();
		var sut = CreateSut(mocker, skyrim.Path, dbContext);
		skyrim.CreateFile(@"Data\enb.dll", "original");
		skyrim.CreateFile(@"Data\Plugins\own.dll", "own");

		// Act
		await sut.BackupBeforeExtraction(Backup, "archive.7z", new SkyrimRelativePath("Data"), CancellationToken.None);
		Extract(skyrim);
		var restored = await sut.Restore(Backup, CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(restored, Is.True);
			Assert.That(File.ReadAllText(skyrim.Combine(@"Data\enb.dll")), Is.EqualTo("original"));
			Assert.That(Directory.Exists(skyrim.Combine(@"Data\Shaders")), Is.False);
			Assert.That(File.Exists(skyrim.Combine(@"Data\Plugins\new.dll")), Is.False);
			Assert.That(File.ReadAllText(skyrim.Combine(@"Data\Plugins\own.dll")), Is.EqualTo("own"));
		});
	}

	[Test]
	public async Task BackupBeforeExtraction_RepeatedAfterExtraction_KeepsOriginalsAndCreatedFiles()
	{
		// Arrange
		using var skyrim = new TempDirectory();
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		SetupArchive(mocker,
			new ArchiveEntry("enb.dll", 3, false),
			new ArchiveEntry(@"Shaders\Deep\fx.hlsl", 2, false),
			new ArchiveEntry(@"Plugins\new.dll", 1, false));
		using var database = new SqliteDatabase();
		using var dbContext = database.CreateMigratedSkyrimLocalContext();
		var sut = CreateSut(mocker, skyrim.Path, dbContext);
		skyrim.CreateFile(@"Data\enb.dll", "original");
		await sut.BackupBeforeExtraction(Backup, "archive.7z", new SkyrimRelativePath("Data"), CancellationToken.None);
		Extract(skyrim);

		// Act: a retry after a failure later in the installation runs the action again.
		await sut.BackupBeforeExtraction(Backup, "archive.7z", new SkyrimRelativePath("Data"), CancellationToken.None);
		Extract(skyrim);
		await sut.Restore(Backup, CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(File.ReadAllText(skyrim.Combine(@"Data\enb.dll")), Is.EqualTo("original"));
			Assert.That(Directory.Exists(skyrim.Combine(@"Data\Shaders")), Is.False);
			Assert.That(Directory.Exists(skyrim.Combine(@"Data\Plugins")), Is.False);
		});
	}

	[Test]
	public async Task BackupBeforeExtraction_EntryOutsideSkyrimDirectory_Throws()
	{
		// Arrange
		using var skyrim = new TempDirectory();
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		SetupArchive(mocker, new ArchiveEntry(@"..\..\outside.dll", 1, false));
		using var database = new SqliteDatabase();
		using var dbContext = database.CreateMigratedSkyrimLocalContext();
		var sut = CreateSut(mocker, skyrim.Path, dbContext);

		// Act
		async Task Act() =>
			await sut.BackupBeforeExtraction(Backup, "archive.7z", new SkyrimRelativePath("Data"), CancellationToken.None);

		// Assert
		Assert.That(Act, Throws.InvalidOperationException);
	}

	[Test]
	public async Task Restore_NoBackup_ReturnsFalse()
	{
		// Arrange
		using var skyrim = new TempDirectory();
		using var database = new SqliteDatabase();
		using var dbContext = database.CreateMigratedSkyrimLocalContext();
		var sut = CreateSut(LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict), skyrim.Path, dbContext);

		// Act
		var restored = await sut.Restore(Backup, CancellationToken.None);

		// Assert
		Assert.That(restored, Is.False);
	}

	[Test]
	public async Task BackupPluginsBeforeMerge_RepeatedAfterMerge_KeepsFirstBackup()
	{
		// Arrange
		using var skyrim = new TempDirectory();
		using var database = new SqliteDatabase();
		using var dbContext = database.CreateMigratedSkyrimLocalContext();
		var sut = CreateSut(LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict), skyrim.Path, dbContext);
		await sut.BackupPluginsBeforeMerge(Backup, new PluginsMergeBackup("Profile", ["*A.esp"], ["*B.esp"]),
			CancellationToken.None);

		// Act
		await sut.BackupPluginsBeforeMerge(Backup, new PluginsMergeBackup("Other", ["*A.esp", "*B.esp"], ["*B.esp"]),
			CancellationToken.None);
		var pluginsBackup = await sut.ReadPluginsMergeBackup(Backup, CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(pluginsBackup?.Profile, Is.EqualTo("Profile"));
			Assert.That(pluginsBackup?.PreviousLines, Is.EqualTo(new[] { "*A.esp" }));
			Assert.That(pluginsBackup?.MergedLines, Is.EqualTo(new[] { "*B.esp" }));
		});
	}

	[Test]
	public async Task ReadPluginsMergeBackup_DeletedBackup_ReturnsNull()
	{
		// Arrange
		using var skyrim = new TempDirectory();
		using var database = new SqliteDatabase();
		using var dbContext = database.CreateMigratedSkyrimLocalContext();
		var sut = CreateSut(LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict), skyrim.Path, dbContext);
		await sut.BackupPluginsBeforeMerge(Backup, new PluginsMergeBackup("Profile", ["*A.esp"], ["*B.esp"]),
			CancellationToken.None);
		sut.Delete(Backup);

		// Act
		var pluginsBackup = await sut.ReadPluginsMergeBackup(Backup, CancellationToken.None);

		// Assert
		Assert.That(pluginsBackup, Is.Null);
	}

	[Test]
	public async Task Delete_Mod_DeletesBackupsOfAllItsVersionsAndKeepsOtherMods()
	{
		// Arrange
		using var skyrim = new TempDirectory();
		using var database = new SqliteDatabase();
		using var dbContext = database.CreateMigratedSkyrimLocalContext();
		var sut = CreateSut(LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict), skyrim.Path, dbContext);
		skyrim.CreateFile("a.txt");
		skyrim.CreateFile("b.txt");
		skyrim.CreateFile("c.txt");
		sut.DeleteWithBackup(Backup, new SkyrimRelativePath("a.txt"), CancellationToken.None);
		var patchBackup = InstallBackupId.ForModVersion("Mod", NuGetVersion.Parse("1.1.0")).ForAction(0, 0);
		sut.DeleteWithBackup(patchBackup, new SkyrimRelativePath("b.txt"), CancellationToken.None);
		var otherBackup = InstallBackupId.ForModVersion("Other", NuGetVersion.Parse("1.0.0")).ForAction(0, 0);
		sut.DeleteWithBackup(otherBackup, new SkyrimRelativePath("c.txt"), CancellationToken.None);

		// Act
		sut.Delete(InstallBackupId.ForMod("Mod"));
		var modRestored = await sut.Restore(Backup, CancellationToken.None);
		var patchRestored = await sut.Restore(patchBackup, CancellationToken.None);
		var otherRestored = await sut.Restore(otherBackup, CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(modRestored, Is.False);
			Assert.That(patchRestored, Is.False);
			Assert.That(otherRestored, Is.True);
		});
	}

	[Test]
	public async Task FinishInstallation_StartedInstallationWithBackup_IsNoLongerUnfinishedAndKeepsBackup()
	{
		// Arrange
		using var skyrim = new TempDirectory();
		using var database = new SqliteDatabase();
		using var dbContext = database.CreateMigratedSkyrimLocalContext();
		var sut = CreateSut(LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict), skyrim.Path, dbContext);
		var id = InstallBackupId.ForModVersion("Mod", NuGetVersion.Parse("1.0.0"));
		var other = InstallBackupId.ForModVersion("Mod", NuGetVersion.Parse("2.0.0"));
		skyrim.CreateFile("a.txt", "original");
		await sut.StartInstallation(id, CancellationToken.None);
		sut.DeleteWithBackup(Backup, new SkyrimRelativePath("a.txt"), CancellationToken.None);
		var unfinishedAfterStart = sut.IsInstallationUnfinished(id);
		var otherUnfinished = sut.IsInstallationUnfinished(other);

		// Act
		sut.FinishInstallation(id);
		var unfinishedAfterFinish = sut.IsInstallationUnfinished(id);
		var restored = await sut.Restore(Backup, CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(unfinishedAfterStart, Is.True);
			Assert.That(otherUnfinished, Is.False);
			Assert.That(unfinishedAfterFinish, Is.False);
			Assert.That(restored, Is.True);
			Assert.That(File.ReadAllText(skyrim.Combine("a.txt")), Is.EqualTo("original"));
		});
	}

	[Test]
	public async Task Delete_ModWithUnfinishedInstallation_InstallationIsNoLongerUnfinished()
	{
		// Arrange
		using var skyrim = new TempDirectory();
		using var database = new SqliteDatabase();
		using var dbContext = database.CreateMigratedSkyrimLocalContext();
		var sut = CreateSut(LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict), skyrim.Path, dbContext);
		var id = InstallBackupId.ForModVersion("Mod", NuGetVersion.Parse("1.0.0"));
		await sut.StartInstallation(id, CancellationToken.None);

		// Act
		sut.Delete(InstallBackupId.ForMod("Mod"));

		// Assert
		Assert.That(sut.IsInstallationUnfinished(id), Is.False);
	}

	[Test]
	public void DeleteAll_BackupsOfSeveralMods_DeletesBackupsDirectory()
	{
		// Arrange
		using var skyrim = new TempDirectory();
		using var database = new SqliteDatabase();
		using var dbContext = database.CreateMigratedSkyrimLocalContext();
		var sut = CreateSut(LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict), skyrim.Path, dbContext);
		skyrim.CreateFile("a.txt");
		skyrim.CreateFile("b.txt");
		sut.DeleteWithBackup(Backup, new SkyrimRelativePath("a.txt"), CancellationToken.None);
		sut.DeleteWithBackup(InstallBackupId.ForModVersion("Other", NuGetVersion.Parse("1.0.0")).ForAction(0, 0),
			new SkyrimRelativePath("b.txt"), CancellationToken.None);

		// Act
		sut.DeleteAll();

		// Assert
		Assert.That(Directory.Exists(skyrim.Combine(@".ExampleLauncher\Backups")), Is.False);
	}

	[Test]
	public async Task FinishInstallation_StartedInstallation_KeepsItsActionsTracked()
	{
		// Arrange
		using var skyrim = new TempDirectory();
		using var database = new SqliteDatabase();
		using var dbContext = database.CreateMigratedSkyrimLocalContext();
		var sut = CreateSut(LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict), skyrim.Path, dbContext);
		await sut.StartInstallation(Backup.Id, CancellationToken.None);
		sut.MarkApplied(Backup);

		// Act
		sut.FinishInstallation(Backup.Id);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.IsTracked(Backup.Id), Is.True);
			Assert.That(sut.IsApplied(Backup), Is.True);
			Assert.That(sut.IsApplied(Backup.Id.ForAction(1, 3)), Is.False);
			Assert.That(sut.IsTracked(InstallBackupId.ForModVersion("Mod", NuGetVersion.Parse("2.0.0"))), Is.False);
		});
	}

	[Test]
	public void Delete_ActionBackup_ForgetsThatTheActionRan()
	{
		// Arrange
		using var skyrim = new TempDirectory();
		using var database = new SqliteDatabase();
		using var dbContext = database.CreateMigratedSkyrimLocalContext();
		var sut = CreateSut(LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict), skyrim.Path, dbContext);
		sut.StartTracking(Backup.Id);
		sut.MarkApplied(Backup);
		sut.MarkApplied(Backup);

		// Act
		sut.Delete(Backup);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.IsApplied(Backup), Is.False);
			Assert.That(sut.IsTracked(Backup.Id), Is.True);
		});
	}

	[Test]
	public void Delete_Mod_ForgetsRecordsOfItsVersionsButNotOfModsWithLongerNames()
	{
		// Arrange
		using var skyrim = new TempDirectory();
		using var database = new SqliteDatabase();
		using var dbContext = database.CreateMigratedSkyrimLocalContext();
		var sut = CreateSut(LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict), skyrim.Path, dbContext);
		var patch = InstallBackupId.ForModVersion("Mod", NuGetVersion.Parse("1.0.1")).ForAction(0, 0);
		var other = InstallBackupId.ForModVersion("Mod_2", NuGetVersion.Parse("1.0.0")).ForAction(0, 0);
		foreach (var backup in new[] { Backup, patch, other })
		{
			sut.StartTracking(backup.Id);
			sut.MarkApplied(backup);
		}

		// Act
		sut.Delete(InstallBackupId.ForMod("Mod"));

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.IsTracked(Backup.Id), Is.False);
			Assert.That(sut.IsApplied(Backup), Is.False);
			Assert.That(sut.IsTracked(patch.Id), Is.False);
			Assert.That(sut.IsApplied(patch), Is.False);
			Assert.That(sut.IsTracked(other.Id), Is.True);
			Assert.That(sut.IsApplied(other), Is.True);
		});
	}

	[Test]
	public void DeleteAll_TrackedActionsOfSeveralMods_ForgetsAllRecords()
	{
		// Arrange
		using var skyrim = new TempDirectory();
		using var database = new SqliteDatabase();
		using var dbContext = database.CreateMigratedSkyrimLocalContext();
		var sut = CreateSut(LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict), skyrim.Path, dbContext);
		var other = InstallBackupId.ForModVersion("Other", NuGetVersion.Parse("1.0.0")).ForAction(0, 0);
		foreach (var backup in new[] { Backup, other })
		{
			sut.StartTracking(backup.Id);
			sut.MarkApplied(backup);
		}

		// Act
		sut.DeleteAll();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.IsTracked(Backup.Id), Is.False);
			Assert.That(sut.IsApplied(Backup), Is.False);
			Assert.That(sut.IsTracked(other.Id), Is.False);
			Assert.That(sut.IsApplied(other), Is.False);
		});
	}

	private static InstallBackupService CreateSut(AutoMocker mocker, string skyrimDirectory,
		SkyrimLocalDbContext dbContext)
	{
		OptionsStubs.Snapshot(mocker, new AppSettings { SkyrimDirectory = skyrimDirectory });
		OptionsStubs.Options(mocker, ProductTestSupport.CreateProduct());
		mocker.Use<ILogger<InstallBackupService>>(NullLogger<InstallBackupService>.Instance);
		mocker.Use(dbContext);
		SetupRealFileSystem(mocker);
		return mocker.CreateInstance<InstallBackupService>();
	}

	/// <summary>Makes the file system mock work on the disk, so the tests check the files the service leaves.</summary>
	private static void SetupRealFileSystem(AutoMocker mocker)
	{
		var fileSystem = mocker.GetMock<IFileSystemUtils>();
		var recursive = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 };
		fileSystem.Setup(x => x.EnsureDirectoryExists(It.IsAny<string>(), false))
			.Callback((string directory, bool _) => Directory.CreateDirectory(directory));
		fileSystem.Setup(x => x.FileExists(It.IsAny<string>())).Returns((string path) => File.Exists(path));
		fileSystem.Setup(x => x.DirectoryExists(It.IsAny<string>())).Returns((string path) => Directory.Exists(path));
		fileSystem.Setup(x => x.IsDirectoryEmpty(It.IsAny<string>()))
			.Returns((string directory) => !Directory.EnumerateFileSystemEntries(directory).Any());
		fileSystem.Setup(x => x.GetFilesRecursive(It.IsAny<string>()))
			.Returns((string directory) => Directory.GetFiles(directory, "*", recursive));
		fileSystem.Setup(x => x.GetDirectoriesRecursive(It.IsAny<string>()))
			.Returns((string directory) => Directory.GetDirectories(directory, "*", recursive));
		fileSystem.Setup(x => x.MoveFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
			.Callback((string source, string destination, bool overwrite) => File.Move(source, destination, overwrite));
		fileSystem.Setup(x => x.ReadAllLines(It.IsAny<string>(), It.IsAny<CancellationToken>()))
			.Returns((string path, CancellationToken cancellationToken) => File.ReadAllLinesAsync(path, cancellationToken));
		fileSystem.Setup(x => x.WriteAllLines(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
			.Returns((string path, IEnumerable<string> lines, CancellationToken cancellationToken) =>
				File.WriteAllLinesAsync(path, lines, cancellationToken));
		fileSystem.Setup(x => x.WriteAllText(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
			.Returns((string path, string contents, CancellationToken cancellationToken) =>
				File.WriteAllTextAsync(path, contents, cancellationToken));
		fileSystem.Setup(x => x.SafeDeleteFile(It.IsAny<string>()))
			.Returns((string path) =>
			{
				if (!File.Exists(path))
				{
					return FileSystemUtils.SafeDeleteResult.NotExist.Instance;
				}

				File.Delete(path);
				return FileSystemUtils.SafeDeleteResult.Success.Instance;
			});
		fileSystem.Setup(x => x.SafeDeleteDirectory(It.IsAny<string>()))
			.Returns((string directory) =>
			{
				if (!Directory.Exists(directory))
				{
					return FileSystemUtils.SafeDeleteResult.NotExist.Instance;
				}

				Directory.Delete(directory, recursive: true);
				return FileSystemUtils.SafeDeleteResult.Success.Instance;
			});
	}

	private static void SetupArchive(AutoMocker mocker, params ArchiveEntry[] entries) =>
		mocker.GetMock<ISevenZipService>()
			.Setup(x => x.ListArchiveEntries("archive.7z", CancellationToken.None))
			.Returns(entries.ToAsyncEnumerable());

	/// <summary>Writes the files of the archive set up by the tests into <c>Data</c>, as 7-Zip would.</summary>
	private static void Extract(TempDirectory skyrim)
	{
		skyrim.CreateFile(@"Data\enb.dll", "new");
		skyrim.CreateFile(@"Data\Shaders\Deep\fx.hlsl", "fx");
		skyrim.CreateFile(@"Data\Plugins\new.dll", "n");
	}
}
