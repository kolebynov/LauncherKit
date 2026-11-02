using IniParser;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Versioning;
using LauncherKit.Core.Backups;
using LauncherKit.Core.Exceptions;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services;
using LauncherKit.Infrastructure;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.Services;

[TestFixture]
public sealed class ModOrganizerServiceTests
{
	[Test]
	public void InstalledMods_ModAndSeparatorDirectories_ReturnsOnlyMods()
	{
		// Arrange
		using var temp = new TempDirectory();
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		OptionsStubs.Snapshot(mocker, new AppSettings { SkyrimDirectory = temp.Path });
		mocker.Use<Microsoft.Extensions.Logging.ILogger<ModOrganizerService>>(NullLogger<ModOrganizerService>.Instance);
		mocker.Use(new FileIniDataParser());
		var sut = mocker.CreateInstance<ModOrganizerService>();
		Directory.CreateDirectory(sut.ModsDirectory);
		Directory.CreateDirectory(Path.Combine(sut.ModsDirectory, "Useful Mod"));
		Directory.CreateDirectory(Path.Combine(sut.ModsDirectory, "Utilities_separator"));

		// Act
		var mods = sut.InstalledMods.Select(x => x.Name).ToArray();

		// Assert
		Assert.That(mods, Is.EqualTo(new[] { "Useful Mod" }));
	}

	[Test]
	public void InstallMod_AlreadyInstalled_RejectsDuplicateBeforeExtraction()
	{
		// Arrange
		using var temp = new TempDirectory();
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		OptionsStubs.Snapshot(mocker, new AppSettings { SkyrimDirectory = temp.Path });
		mocker.Use<Microsoft.Extensions.Logging.ILogger<ModOrganizerService>>(NullLogger<ModOrganizerService>.Instance);
		mocker.Use(new FileIniDataParser());
		var sut = mocker.CreateInstance<ModOrganizerService>();
		var modDirectory = Directory.CreateDirectory(Path.Combine(sut.ModsDirectory, "Existing"));
		File.WriteAllText(Path.Combine(modDirectory.FullName, "Existing.esp"), string.Empty);

		// Act
		// Assert
		Assert.That(async () => await sut.InstallMod("Existing", "archive.7z", null, CancellationToken.None),
			Throws.ArgumentException.With.Property("ParamName").EqualTo("modName"));
	}

	[Test]
	public async Task PlaceMod_ExistingSeparator_MovesModToEndOfItsGroupKeepingState()
	{
		// Arrange
		using var temp = new TempDirectory();
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		SetupWithRealModListEdits(mocker, temp.Path);
		var sut = mocker.CreateInstance<ModOrganizerService>();
		var modListPath = CreateModList(temp.Path,
			"# header", "+Mod", "+Top", "-First_separator", "+Grouped", "-Second_separator", "+Bottom");
		Directory.CreateDirectory(Path.Combine(sut.ModsDirectory, "Second_separator"));
		File.WriteAllText(Path.Combine(sut.ModsDirectory, "Second_separator", "meta.ini"), "[General]\nmodid=0\n");

		// Act
		await sut.PlaceMod("Mod", new ModPlacement.UnderSeparator("Second"), CancellationToken.None);

		// Assert
		// Mod Organizer shows the list bottom-up, so Mod is shown last under Second, after Grouped.
		Assert.That(File.ReadAllLines(modListPath),
			Is.EqualTo(new[] { "# header", "+Top", "-First_separator", "+Mod", "+Grouped", "-Second_separator", "+Bottom" }));
	}

	[Test]
	public async Task PlaceMod_MissingSeparatorWithoutUserModsSeparator_CreatesItAtEndOfListWithModUnderIt()
	{
		// Arrange
		using var temp = new TempDirectory();
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		SetupWithRealModListEdits(mocker, temp.Path);
		var sut = mocker.CreateInstance<ModOrganizerService>();
		var modListPath = CreateModList(temp.Path, "# header", "+Other", "-First_separator");

		// Act
		await sut.PlaceMod("Mod", new ModPlacement.UnderSeparator("New"), CancellationToken.None);

		// Assert
		using (Assert.EnterMultipleScope())
		{
			Assert.That(File.ReadAllLines(modListPath),
				Is.EqualTo(new[] { "# header", "-Mod", "-New_separator", "+Other", "-First_separator" }));
			Assert.That(File.Exists(Path.Combine(sut.ModsDirectory, "New_separator", "meta.ini")), Is.True);
			Assert.That(sut.InstalledMods, Is.Empty);
		}
	}

	[Test]
	public async Task PlaceMod_MissingSeparator_CreatesItRightAboveUserModsSeparatorWithModUnderIt()
	{
		// Arrange
		using var temp = new TempDirectory();
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		SetupWithRealModListEdits(mocker, temp.Path);
		var sut = mocker.CreateInstance<ModOrganizerService>();
		var modListPath = CreateModList(temp.Path,
			"# header", "+User Mod", "-USER MODS_separator", "+Interface", "-ИНТЕРФЕЙС_separator");

		// Act
		await sut.PlaceMod("Mod", new ModPlacement.UnderSeparator("New"), CancellationToken.None);

		// Assert
		// Shown bottom-up: ИНТЕРФЕЙС, Interface, New, Mod, USER MODS, User Mod.
		Assert.That(File.ReadAllLines(modListPath), Is.EqualTo(new[]
		{
			"# header", "+User Mod", "-USER MODS_separator", "-Mod", "-New_separator", "+Interface", "-ИНТЕРФЕЙС_separator",
		}));
	}

	// Shown bottom-up, so Below (higher priority) is the line before the neighbor and Above the line after it.
	[TestCase(ModListSide.Below, new[] { "# header", "+Top", "+Mod", "+Neighbor", "+Bottom" })]
	[TestCase(ModListSide.Above, new[] { "# header", "+Top", "+Neighbor", "+Mod", "+Bottom" })]
	public async Task PlaceMod_NextToListedMod_MovesModDirectlyBesideItKeepingState(ModListSide side, string[] expected)
	{
		// Arrange
		using var temp = new TempDirectory();
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		SetupWithRealModListEdits(mocker, temp.Path);
		var sut = mocker.CreateInstance<ModOrganizerService>();
		var modListPath = CreateModList(temp.Path, "# header", "+Mod", "+Top", "+Neighbor", "+Bottom");

		// Act
		await sut.PlaceMod("Mod", new ModPlacement.NextToMod("Neighbor", side), CancellationToken.None);

		// Assert
		Assert.That(File.ReadAllLines(modListPath), Is.EqualTo(expected));
	}

	[Test]
	public async Task PlaceMod_NextToModNotListed_LeavesModListUnchanged()
	{
		// Arrange
		using var temp = new TempDirectory();
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		SetupWithRealModListEdits(mocker, temp.Path);
		var sut = mocker.CreateInstance<ModOrganizerService>();
		var modListPath = CreateModList(temp.Path, "# header", "+Mod", "+Other");

		// Act
		await sut.PlaceMod("Mod", new ModPlacement.NextToMod("Missing", ModListSide.Below), CancellationToken.None);

		// Assert
		Assert.That(File.ReadAllLines(modListPath), Is.EqualTo(new[] { "# header", "+Mod", "+Other" }));
	}

	[Test]
	public async Task MergePlugins_PartialPluginsFile_MergesItIntoPluginsAndRebuildsLoadOrder()
	{
		// Arrange
		using var temp = new TempDirectory();
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		SetupWithRealModListEdits(mocker, temp.Path);
		var sut = mocker.CreateInstance<ModOrganizerService>();
		var profileDirectory = Path.GetDirectoryName(CreateProfile(temp.Path, "Profile", "LocalSaves=true"))!;
		var pluginsPath = Path.Combine(profileDirectory, "plugins.txt");
		var loadOrderPath = Path.Combine(profileDirectory, "loadorder.txt");
		File.WriteAllLines(pluginsPath, ["# header", "*Base.esp", "*Other.esp", "Patch.esp"]);
		File.WriteAllLines(loadOrderPath, ["# header", "Skyrim.esm", "Base.esp", "Other.esp", "Patch.esp"]);
		var mergedPath = Path.Combine(temp.Path, "merged plugins.txt");
		File.WriteAllLines(mergedPath, ["*Base.esp", "*Patch.esp", "*New.esp"]);

		// Act
		await sut.MergePlugins(mergedPath, null, CancellationToken.None);

		// Assert
		using (Assert.EnterMultipleScope())
		{
			Assert.That(File.ReadAllLines(pluginsPath),
				Is.EqualTo(new[] { "# header", "*Base.esp", "*Other.esp", "*Patch.esp", "*New.esp" }));
			Assert.That(File.ReadAllLines(loadOrderPath),
				Is.EqualTo(new[] { "# header", "Skyrim.esm", "Base.esp", "Other.esp", "Patch.esp", "New.esp" }));
		}
	}

	[Test]
	public async Task MergePlugins_WithBackup_BacksUpProfileAndListsBeforeChangingThem()
	{
		// Arrange
		using var temp = new TempDirectory();
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		SetupWithRealModListEdits(mocker, temp.Path);
		var sut = mocker.CreateInstance<ModOrganizerService>();
		var profileDirectory = Path.GetDirectoryName(CreateProfile(temp.Path, "Profile", "LocalSaves=true"))!;
		var pluginsPath = Path.Combine(profileDirectory, "plugins.txt");
		File.WriteAllLines(pluginsPath, ["*Base.esp"]);
		var mergedPath = Path.Combine(temp.Path, "merged plugins.txt");
		File.WriteAllLines(mergedPath, ["*New.esp"]);
		var backup = InstallBackupId.ForMod("Mod").ForAction(0, 0);
		PluginsMergeBackup? pluginsBackup = null;
		string[]? pluginsWhenBackedUp = null;
		mocker.GetMock<IInstallBackupService>()
			.Setup(x => x.BackupPluginsBeforeMerge(backup, It.IsAny<PluginsMergeBackup>(), CancellationToken.None))
			.Callback((InstallActionBackup _, PluginsMergeBackup value, CancellationToken _) =>
			{
				pluginsBackup = value;
				pluginsWhenBackedUp = File.ReadAllLines(pluginsPath);
			})
			.Returns(Task.CompletedTask);

		// Act
		await sut.MergePlugins(mergedPath, backup, CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(pluginsBackup?.Profile, Is.EqualTo("Profile"));
			Assert.That(pluginsBackup?.PreviousLines, Is.EqualTo(new[] { "*Base.esp" }));
			Assert.That(pluginsBackup?.MergedLines, Is.EqualTo(new[] { "*New.esp" }));
			Assert.That(pluginsWhenBackedUp, Is.EqualTo(new[] { "*Base.esp" }));
			Assert.That(File.ReadAllLines(pluginsPath), Is.EqualTo(new[] { "*Base.esp", "*New.esp" }));
		});
	}

	[Test]
	public async Task RevertPluginsMerge_Backup_RevertsMergeInBackedUpProfileAndDeletesBackup()
	{
		// Arrange
		using var temp = new TempDirectory();
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		SetupWithRealModListEdits(mocker, temp.Path);
		var sut = mocker.CreateInstance<ModOrganizerService>();
		var selectedProfileDirectory = Path.GetDirectoryName(CreateProfile(temp.Path, "Selected", "LocalSaves=true"))!;
		var profileDirectory = Directory.CreateDirectory(
			Path.Combine(Path.GetDirectoryName(selectedProfileDirectory)!, "Graph pack")).FullName;
		var pluginsPath = Path.Combine(profileDirectory, "plugins.txt");
		var loadOrderPath = Path.Combine(profileDirectory, "loadorder.txt");
		File.WriteAllLines(pluginsPath, ["*Base.esp", "*New.esp", "*User.esp", "Patch.esp"]);
		File.WriteAllLines(loadOrderPath, ["Skyrim.esm", "Base.esp", "New.esp", "User.esp", "Patch.esp"]);
		var backup = InstallBackupId.ForMod("Mod").ForAction(0, 0);
		var backups = mocker.GetMock<IInstallBackupService>();
		backups.Setup(x => x.ReadPluginsMergeBackup(backup, CancellationToken.None))
			.ReturnsAsync(new PluginsMergeBackup("Graph pack", ["*Base.esp", "*Patch.esp"], ["*New.esp", "Patch.esp"]));
		backups.Setup(x => x.Delete(backup));

		// Act
		var reverted = await sut.RevertPluginsMerge(backup, CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(reverted, Is.True);
			Assert.That(File.ReadAllLines(pluginsPath), Is.EqualTo(new[] { "*Base.esp", "*Patch.esp", "*User.esp" }));
			Assert.That(File.ReadAllLines(loadOrderPath),
				Is.EqualTo(new[] { "Skyrim.esm", "Base.esp", "Patch.esp", "User.esp" }));
			backups.Verify(x => x.Delete(backup), Times.Once);
		});
	}

	[Test]
	public async Task CreateOrUpdateMetaIniFile_CommentsNotEmpty_KeepsCommentsAndUpdatesVersion()
	{
		// Arrange
		using var temp = new TempDirectory();
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		OptionsStubs.Snapshot(mocker, new AppSettings { SkyrimDirectory = temp.Path });
		mocker.Use<Microsoft.Extensions.Logging.ILogger<ModOrganizerService>>(NullLogger<ModOrganizerService>.Instance);
		mocker.Use(new FileIniDataParser());
		var sut = mocker.CreateInstance<ModOrganizerService>();
		var modDirectory = Directory.CreateDirectory(Path.Combine(sut.ModsDirectory, "Mod"));
		await File.WriteAllTextAsync(Path.Combine(modDirectory.FullName, "meta.ini"),
			"[General]\ncomments=User note\nversion=1.0.0\n");

		// Act
		await sut.CreateOrUpdateMetaIniFile("Mod", "mod.7z", new NuGetVersion(2, 0, 0), "Feed name", CancellationToken.None);

		// Assert
		var mod = sut.InstalledMods.Single();
		using (Assert.EnterMultipleScope())
		{
			Assert.That(mod.Description, Is.EqualTo("User note"));
			Assert.That(mod.Version, Is.EqualTo("2.0.0"));
		}
	}

	[Test]
	public async Task CreateOrUpdateMetaIniFile_CommentsEmpty_WritesComments()
	{
		// Arrange
		using var temp = new TempDirectory();
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		OptionsStubs.Snapshot(mocker, new AppSettings { SkyrimDirectory = temp.Path });
		mocker.Use<Microsoft.Extensions.Logging.ILogger<ModOrganizerService>>(NullLogger<ModOrganizerService>.Instance);
		mocker.Use(new FileIniDataParser());
		var sut = mocker.CreateInstance<ModOrganizerService>();
		var modDirectory = Directory.CreateDirectory(Path.Combine(sut.ModsDirectory, "Mod"));
		await File.WriteAllTextAsync(Path.Combine(modDirectory.FullName, "meta.ini"), "[General]\ncomments=\n");

		// Act
		await sut.CreateOrUpdateMetaIniFile("Mod", "mod.7z", new NuGetVersion(2, 0, 0), "Feed name", CancellationToken.None);

		// Assert
		Assert.That(sut.InstalledMods.Single().Description, Is.EqualTo("Feed name"));
	}

	[TestCase(@"C:\Downloads\new.7z", "C:/Downloads/new.7z")]
	[TestCase(null, "C:/Downloads/old.7z")]
	public async Task CreateOrUpdateMetaIniFile_InstallationFile_WritesItOrKeepsRecordedOneWhenUnknown(
		string? installationFile, string expected)
	{
		// Arrange
		using var temp = new TempDirectory();
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		OptionsStubs.Snapshot(mocker, new AppSettings { SkyrimDirectory = temp.Path });
		mocker.Use<Microsoft.Extensions.Logging.ILogger<ModOrganizerService>>(NullLogger<ModOrganizerService>.Instance);
		var parser = new FileIniDataParser();
		mocker.Use(parser);
		var sut = mocker.CreateInstance<ModOrganizerService>();
		var modDirectory = Directory.CreateDirectory(Path.Combine(sut.ModsDirectory, "Mod"));
		var metaIniPath = Path.Combine(modDirectory.FullName, "meta.ini");
		await File.WriteAllTextAsync(metaIniPath, "[General]\ninstallationFile=C:/Downloads/old.7z\nversion=1.0.0\n");

		// Act
		await sut.CreateOrUpdateMetaIniFile("Mod", installationFile, new NuGetVersion(1, 0, 1), "Mod", CancellationToken.None);

		// Assert
		Assert.That(parser.ReadFile(metaIniPath)["General"]["installationFile"], Is.EqualTo(expected));
	}

	[Test]
	public async Task DeleteOldSaves_MoreSavesThanKept_DeletesOldestSavesWithTheirCoSavesAndReportsTheirSize()
	{
		// Arrange
		using var temp = new TempDirectory();
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		SetupWithRealFileDeletion(mocker, temp.Path);
		var sut = mocker.CreateInstance<ModOrganizerService>();
		var savesDirectory = CreateProfile(temp.Path, "Profile", "LocalSaves=true");
		var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		for (var i = 1; i <= 4; i++)
		{
			// Names sort opposite to write times, so only the write time can pick the newest saves.
			CreateSave(savesDirectory, $"Save {5 - i}", start.AddHours(i), saveSize: i * 100, coSaveSize: i * 10);
		}

		// Written after the oldest kept save, so it stays.
		CreateCoSave(savesDirectory, "Orphan", start.AddHours(5), size: 1000);

		// Act
		var deleted = await sut.DeleteOldSaves(2, CancellationToken.None);

		// Assert
		var remaining = Directory.EnumerateFiles(savesDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal);
		using (Assert.EnterMultipleScope())
		{
			// Saves 4 and 3 (i = 1, 2): 100 + 10 + 200 + 20 bytes; the kept saves and the orphan co-save do not count.
			Assert.That(deleted, Is.EqualTo(new DeletedSavesInfo(2, 330, null)));
			Assert.That(remaining,
				Is.EqualTo(new[] { "Orphan.skse", "Save 1.ess", "Save 1.skse", "Save 2.ess", "Save 2.skse" }));
		}
	}

	[Test]
	public async Task DeleteOldSaves_OrphanCoSavesAroundOldestKeptSave_DeletesOnlyThoseWrittenBeforeIt()
	{
		// Arrange
		using var temp = new TempDirectory();
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		SetupWithRealFileDeletion(mocker, temp.Path);
		var sut = mocker.CreateInstance<ModOrganizerService>();
		var savesDirectory = CreateProfile(temp.Path, "Profile", "LocalSaves=true");
		var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		CreateSave(savesDirectory, "Newest", start.AddHours(5), saveSize: 300, coSaveSize: 30);
		// The kept save's co-save is older than its game file, so it sets the boundary.
		CreateSave(savesDirectory, "Kept", start.AddHours(4), saveSize: 200, coSaveSize: 20,
			coSaveWriteOffset: TimeSpan.FromHours(-2));
		CreateSave(savesDirectory, "Old", start.AddHours(1), saveSize: 100, coSaveSize: 10);
		CreateCoSave(savesDirectory, "Renamed old", start.AddHours(2).AddSeconds(-1), size: 1000);
		CreateCoSave(savesDirectory, "Renamed at boundary", start.AddHours(2), size: 2000);

		// Act
		var deleted = await sut.DeleteOldSaves(2, CancellationToken.None);

		// Assert
		var remaining = Directory.EnumerateFiles(savesDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal);
		using (Assert.EnterMultipleScope())
		{
			// Old with its co-save and the orphan written before the boundary: 100 + 10 + 1000 bytes.
			Assert.That(deleted, Is.EqualTo(new DeletedSavesInfo(1, 1110, null)));
			Assert.That(remaining, Is.EqualTo(new[]
			{
				"Kept.ess", "Kept.skse", "Newest.ess", "Newest.skse", "Renamed at boundary.skse",
			}));
		}
	}

	[Test]
	public async Task DeleteOldSaves_NoneKept_DeletesEverySaveAndOrphanCoSave()
	{
		// Arrange
		using var temp = new TempDirectory();
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		SetupWithRealFileDeletion(mocker, temp.Path);
		var sut = mocker.CreateInstance<ModOrganizerService>();
		var savesDirectory = CreateProfile(temp.Path, "Profile", "LocalSaves=true");
		File.WriteAllBytes(Path.Combine(savesDirectory, "Save 1.ess"), new byte[50]);
		File.WriteAllBytes(Path.Combine(savesDirectory, "Orphan.skse"), new byte[7]);

		// Act
		var deleted = await sut.DeleteOldSaves(0, CancellationToken.None);

		// Assert
		using (Assert.EnterMultipleScope())
		{
			Assert.That(deleted, Is.EqualTo(new DeletedSavesInfo(1, 57, null)));
			Assert.That(Directory.EnumerateFiles(savesDirectory), Is.Empty);
		}
	}

	[Test]
	public async Task DeleteOldSaves_CoSaveCannotBeDeleted_ReturnsSavesDeletedBeforeTheErrorWithIt()
	{
		// Arrange
		using var temp = new TempDirectory();
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		SetupWithRealFileDeletion(mocker, temp.Path);
		var sut = mocker.CreateInstance<ModOrganizerService>();
		var savesDirectory = CreateProfile(temp.Path, "Profile", "LocalSaves=true");
		var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		CreateSave(savesDirectory, "Newest", start.AddHours(3), saveSize: 300, coSaveSize: 30);
		CreateSave(savesDirectory, "Locked", start.AddHours(2), saveSize: 200, coSaveSize: 20);
		CreateSave(savesDirectory, "Oldest", start.AddHours(1), saveSize: 100, coSaveSize: 10);
		var lockError = new IOException("locked");
		mocker.GetMock<IFileSystemUtils>()
			.Setup(x => x.SafeDeleteFile(Path.Combine(savesDirectory, "Locked.skse")))
			.Returns(new FileSystemUtils.SafeDeleteResult.Error(lockError));

		// Act
		var deleted = await sut.DeleteOldSaves(0, CancellationToken.None);

		// Assert
		var remaining = Directory.EnumerateFiles(savesDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal);
		using (Assert.EnterMultipleScope())
		{
			// Newest and Locked lost their game files; Locked's co-save stays and is not counted.
			Assert.That(deleted.Count, Is.EqualTo(2));
			Assert.That(deleted.FreedBytes, Is.EqualTo(530));
			Assert.That(deleted.Error, Is.InstanceOf<SaveDeletionException>()
				.With.Property(nameof(SaveDeletionException.FileName)).EqualTo("Locked.skse")
				.With.Property(nameof(Exception.InnerException)).SameAs(lockError));
			Assert.That(remaining, Is.EqualTo(new[] { "Locked.skse", "Oldest.ess", "Oldest.skse" }));
		}
	}

	[Test]
	public void DeleteOldSaves_LocalSavesDisabled_ThrowsWithoutDeleting()
	{
		// Arrange
		using var temp = new TempDirectory();
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		SetupWithRealFileDeletion(mocker, temp.Path);
		var sut = mocker.CreateInstance<ModOrganizerService>();
		var savesDirectory = CreateProfile(temp.Path, "Profile", "LocalSaves=false");
		File.WriteAllText(Path.Combine(savesDirectory, "Save 1.ess"), string.Empty);

		// Act
		// Assert
		Assert.That(async () => await sut.DeleteOldSaves(0, CancellationToken.None),
			Throws.TypeOf<LocalSavesDisabledException>()
				.With.Property(nameof(LocalSavesDisabledException.Profile)).EqualTo("Profile"));
		Assert.That(File.Exists(Path.Combine(savesDirectory, "Save 1.ess")), Is.True);
	}

	private static void SetupWithRealModListEdits(AutoMocker mocker, string skyrimDirectory)
	{
		OptionsStubs.Snapshot(mocker, new AppSettings { SkyrimDirectory = skyrimDirectory });
		mocker.Use<Microsoft.Extensions.Logging.ILogger<ModOrganizerService>>(NullLogger<ModOrganizerService>.Instance);
		mocker.Use(new FileIniDataParser());
		mocker.GetMock<IFileSystemUtils>()
			.Setup(x => x.ModifyFileLines(It.IsAny<string>(), It.IsAny<Func<string[], IEnumerable<string>>>(),
				It.IsAny<CancellationToken>()))
			.Returns((string path, Func<string[], IEnumerable<string>> modify, CancellationToken cancellationToken) =>
				File.WriteAllLinesAsync(path, modify(File.ReadAllLines(path)), cancellationToken));
		var fileSystem = mocker.GetMock<IFileSystemUtils>();
		fileSystem.Setup(x => x.FileExists(It.IsAny<string>())).Returns((string path) => File.Exists(path));
		fileSystem.Setup(x => x.ReadAllLines(It.IsAny<string>(), It.IsAny<CancellationToken>()))
			.Returns((string path, CancellationToken cancellationToken) => File.ReadAllLinesAsync(path, cancellationToken));
		fileSystem.Setup(x => x.WriteAllLines(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
			.Returns((string path, IEnumerable<string> lines, CancellationToken cancellationToken) =>
				File.WriteAllLinesAsync(path, lines, cancellationToken));
	}

	/// <summary>Creates the selected profile <c>Profile</c> with the given mod list and returns its path.</summary>
	private static string CreateModList(string skyrimDirectory, params string[] lines)
	{
		var profileDirectory = Path.GetDirectoryName(CreateProfile(skyrimDirectory, "Profile", "LocalSaves=true"))!;
		var modListPath = Path.Combine(profileDirectory, "modlist.txt");
		File.WriteAllLines(modListPath, lines);
		return modListPath;
	}

	private static void SetupWithRealFileDeletion(AutoMocker mocker, string skyrimDirectory)
	{
		OptionsStubs.Snapshot(mocker, new AppSettings { SkyrimDirectory = skyrimDirectory });
		mocker.Use<Microsoft.Extensions.Logging.ILogger<ModOrganizerService>>(NullLogger<ModOrganizerService>.Instance);
		mocker.Use(new FileIniDataParser());
		mocker.GetMock<IFileSystemUtils>()
			.Setup(x => x.SafeDeleteFile(It.IsAny<string>()))
			.Returns((string path) =>
			{
				if (!File.Exists(path))
				{
					return FileSystemUtils.SafeDeleteResult.NotExist.Instance;
				}

				File.Delete(path);
				return FileSystemUtils.SafeDeleteResult.Success.Instance;
			});
	}

	/// <summary>Creates the selected profile with the given <c>[General]</c> setting and returns its saves directory.</summary>
	private static string CreateProfile(string skyrimDirectory, string profile, string generalSetting)
	{
		var modOrganizerDirectory = new AppSettings { SkyrimDirectory = skyrimDirectory }.GetModOrganizerDirectory(ProductTestSupport.CreateProduct())!;
		var profileDirectory = Path.Combine(modOrganizerDirectory, "profiles", profile);
		var savesDirectory = Directory.CreateDirectory(Path.Combine(profileDirectory, "saves")).FullName;
		File.WriteAllText(Path.Combine(modOrganizerDirectory, "ModOrganizer.ini"),
			$"[General]\nselected_profile=@ByteArray({profile})\n");
		File.WriteAllText(Path.Combine(profileDirectory, "settings.ini"), $"[General]\n{generalSetting}\n");
		return savesDirectory;
	}

	private static void CreateSave(string savesDirectory, string name, DateTime lastWriteTimeUtc, int saveSize,
		int coSaveSize, TimeSpan coSaveWriteOffset = default)
	{
		var savePath = Path.Combine(savesDirectory, $"{name}.ess");
		File.WriteAllBytes(savePath, new byte[saveSize]);
		File.SetLastWriteTimeUtc(savePath, lastWriteTimeUtc);
		CreateCoSave(savesDirectory, name, lastWriteTimeUtc + coSaveWriteOffset, coSaveSize);
	}

	private static void CreateCoSave(string savesDirectory, string name, DateTime lastWriteTimeUtc, int size)
	{
		var coSavePath = Path.Combine(savesDirectory, $"{name}.skse");
		File.WriteAllBytes(coSavePath, new byte[size]);
		File.SetLastWriteTimeUtc(coSavePath, lastWriteTimeUtc);
	}
}
