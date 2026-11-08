using LauncherKit.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Versioning;
using R3;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.AppTasks.InstallSteps;
using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.Core.Backups;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.Install;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Tests.Services;

[TestFixture]
public sealed class InstallAppTaskProviderTests
{
	[Test]
	public async Task CreateModInstallAppTask_InstalledVersion_RollsBackItThenInstallsArchiveBeforeSteps()
	{
		// Arrange
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		mocker.Use<ILogger<InstallAppTaskProvider>>(NullLogger<InstallAppTaskProvider>.Instance);
		var prerequisites = mocker.GetMock<IPrerequisitesAppTaskProvider>();
		prerequisites.SetupGet(x => x.InstallVcRedistTasks).Returns([]);
		prerequisites.Setup(x => x.CreateInstallSevenZipTask(CancellationToken.None))
			.ReturnsAsync(new AppTask("InstallSevenZip", new AppTaskDescription.Custom("7-Zip"), _ => [], [], null));
		mocker.GetMock<IFileStorageService>().Setup(x => x.GetFilePath("mod-2.0.0.7z")).Returns(@"C:\Downloads\mod-2.0.0.7z");
		var modOrganizer = mocker.GetMock<IModOrganizerService>();
		modOrganizer.Setup(x => x.IsModInstalled("Mod")).Returns(true);
		modOrganizer.SetupGet(x => x.InstalledMods).Returns(
			[new ModInfo("Mod", new Lazy<MetaIniData?>(() => new MetaIniData { Comments = null, Version = "1.0.0" }))]);
		modOrganizer.Setup(x => x.UninstallMod("Mod", CancellationToken.None)).Returns(Task.CompletedTask);
		modOrganizer.Setup(x => x.CreateOrUpdateMetaIniFile("Mod", @"C:\Downloads\mod-2.0.0.7z", NuGetVersion.Parse("2.0.0"),
				"Mod", CancellationToken.None))
			.Returns(Task.CompletedTask);
		var mod = CreateMod([new InstallStep { Actions = [], CanRollback = true }], [new InstallStep { Actions = [] }]);
		var archive = mod.Items[1].ArchiveDownloadInfo;
		var factory = mocker.GetMock<IMicroTasksFactory>();
		factory.Setup(x => x.CreateDownloadFileMicroTask(It.Is<DownloadFileSource>(source => ReferenceEquals(source.Value, archive))))
			.Returns(Marker("download 2.0.0"));
		factory.Setup(x => x.CreateInstallModMicroTask("Mod", "mod-2.0.0.7z", new ModPlacement.UnderSeparator("Extras"))).Returns(Marker("install 2.0.0"));
		var installSteps = UseInstallStepsMicroTasksFactory(mocker);
		installSteps.Setup(x => x.CreateRollbackMicroTasks(mod.Items[0].InstallSteps!, BackupId("1.0.0"))).Returns([Marker("rollback 1.0.0")]);
		installSteps.Setup(x => x.CreateInstallMicroTasks(mod.Items[1].InstallSteps!, BackupId("2.0.0"))).Returns([Marker("steps 2.0.0")]);
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();
		var appTask = await sut.CreateModInstallAppTask(mod, NuGetVersion.Parse("2.0.0"), CancellationToken.None);

		// Act
		var names = await RunMicroTasks(mocker, appTask);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(names, Is.EqualTo(new object[]
		{
				new MicroTaskDescription.Custom("rollback 1.0.0"), MicroTaskDescription.DeletePreviousVersion.Instance, MicroTaskDescription.DeleteBackups.Instance, new MicroTaskDescription.Custom("download 2.0.0"),
				MicroTaskDescription.PreparingInstallation.Instance, new MicroTaskDescription.Custom("install 2.0.0"), new MicroTaskDescription.Custom("steps 2.0.0"), MicroTaskDescription.UpdateModMetadata.Instance, MicroTaskDescription.FinishInstallation.Instance,
			}));
			mocker.GetMock<IInstallBackupService>().Verify(x => x.StartInstallation(BackupId("2.0.0"), CancellationToken.None), Times.Once);
			mocker.GetMock<IInstallBackupService>().Verify(x => x.FinishInstallation(BackupId("2.0.0")), Times.Once);
			modOrganizer.Verify(x => x.UninstallMod("Mod", CancellationToken.None), Times.Once);
			modOrganizer.Verify(x => x.CreateOrUpdateMetaIniFile("Mod", @"C:\Downloads\mod-2.0.0.7z",
				NuGetVersion.Parse("2.0.0"), "Mod", CancellationToken.None), Times.Once);
		});
	}

	[Test]
	public async Task CreateModInstallAppTask_NewModWithPatches_InstallsVersionThenPatchesWithDownloadedArchivePath()
	{
		// Arrange
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		var prerequisites = mocker.GetMock<IPrerequisitesAppTaskProvider>();
		prerequisites.SetupGet(x => x.InstallVcRedistTasks).Returns([]);
		prerequisites.Setup(x => x.CreateInstallSevenZipTask(CancellationToken.None))
			.ReturnsAsync(new AppTask("InstallSevenZip", new AppTaskDescription.Custom("7-Zip"), _ => [], [], null));
		// The archive is stored only once the task downloads it, after the microtasks were built.
		var downloaded = false;
		mocker.GetMock<IFileStorageService>().Setup(x => x.GetFilePath("mod-2.0.0.7z"))
			.Returns(() => downloaded ? @"C:\Downloads\mod-2.0.0.7z" : null);
		var modOrganizer = mocker.GetMock<IModOrganizerService>();
		modOrganizer.Setup(x => x.IsModInstalled("Mod")).Returns(false);
		modOrganizer.Setup(x => x.CreateOrUpdateMetaIniFile("Mod", @"C:\Downloads\mod-2.0.0.7z", NuGetVersion.Parse("2.0.0"),
				"Mod", CancellationToken.None))
			.Returns(Task.CompletedTask);
		var mod = CreateMod([], [new InstallStep { Actions = [] }], v2Patches: [CreatePatch("2.2.0"), CreatePatch("2.1.0")]);
		var factory = mocker.GetMock<IMicroTasksFactory>();
		factory.Setup(x => x.CreateDownloadFileMicroTask(It.Is<DownloadFileSource>(source => ReferenceEquals(source.Value, mod.Items[1].ArchiveDownloadInfo))))
			.Returns(new DelegateMicroTask(new MicroTaskDescription.Custom("download 2.0.0"), (_, _) =>
			{
				downloaded = true;
				return ValueTask.CompletedTask;
			}));
		factory.Setup(x => x.CreateInstallModMicroTask("Mod", "mod-2.0.0.7z", new ModPlacement.UnderSeparator("Extras"))).Returns(Marker("install 2.0.0"));
		var installSteps = UseInstallStepsMicroTasksFactory(mocker);
		installSteps.Setup(x => x.CreateInstallMicroTasks(mod.Items[1].InstallSteps!, BackupId("2.0.0"))).Returns([Marker("steps 2.0.0")]);
		SetupPatches(mocker, installSteps, mod.Items[1].Patches!);
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();
		var appTask = await sut.CreateModInstallAppTask(mod, NuGetVersion.Parse("2.0.0"), CancellationToken.None);

		// Act
		var names = await RunMicroTasks(mocker, appTask);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(names, Is.EqualTo(new object[]
		{
				MicroTaskDescription.DeleteBackups.Instance, new MicroTaskDescription.Custom("download 2.0.0"), MicroTaskDescription.PreparingInstallation.Instance, new MicroTaskDescription.Custom("install 2.0.0"), new MicroTaskDescription.Custom("steps 2.0.0"),
				MicroTaskDescription.UpdateModMetadata.Instance, MicroTaskDescription.FinishInstallation.Instance,
				new MicroTaskDescription.Custom("download 2.1.0"), new MicroTaskDescription.Custom("extract 2.1.0"), new MicroTaskDescription.Custom("steps 2.1.0"), MicroTaskDescription.UpdateModMetadata.Instance,
				new MicroTaskDescription.Custom("download 2.2.0"), new MicroTaskDescription.Custom("extract 2.2.0"), new MicroTaskDescription.Custom("steps 2.2.0"), MicroTaskDescription.UpdateModMetadata.Instance,
			}));
			foreach (var version in new[] { "2.0.0", "2.1.0", "2.2.0" })
			{
				modOrganizer.Verify(x => x.CreateOrUpdateMetaIniFile("Mod", @"C:\Downloads\mod-2.0.0.7z",
					NuGetVersion.Parse(version), "Mod", CancellationToken.None), Times.Once);
			}
		});
	}

	[Test]
	public async Task CreateModInstallAppTask_InstalledVersionMissingPatches_AppliesOnlyMissingPatchesWithoutReinstalling()
	{
		// Arrange
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		var prerequisites = mocker.GetMock<IPrerequisitesAppTaskProvider>();
		prerequisites.SetupGet(x => x.InstallVcRedistTasks).Returns([]);
		prerequisites.Setup(x => x.CreateInstallSevenZipTask(CancellationToken.None))
			.ReturnsAsync(new AppTask("InstallSevenZip", new AppTaskDescription.Custom("7-Zip"), _ => [], [], null));
		mocker.GetMock<IFileStorageService>().Setup(x => x.GetFilePath("mod-2.0.0.7z")).Returns(@"C:\Downloads\mod-2.0.0.7z");
		var modOrganizer = mocker.GetMock<IModOrganizerService>();
		modOrganizer.Setup(x => x.IsModInstalled("Mod")).Returns(true);
		modOrganizer.SetupGet(x => x.InstalledMods).Returns(
			[new ModInfo("Mod", new Lazy<MetaIniData?>(() => new MetaIniData { Comments = null, Version = "2.1.0" }))]);
		var mod = CreateMod([], [new InstallStep { Actions = [] }], v2Patches: [CreatePatch("2.1.0"), CreatePatch("2.2.0")]);
		var installSteps = UseInstallStepsMicroTasksFactory(mocker);
		SetupPatches(mocker, installSteps, [mod.Items[1].Patches![1]]);
		mocker.GetMock<IModpackVersionProvider>().SetupGet(x => x.InstalledModpackVersion).Returns(NuGetVersion.Parse("3.0.0"));
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();
		var appTask = await sut.CreateModInstallAppTask(mod, null, CancellationToken.None);

		// Act
		var names = await RunMicroTasks(mocker, appTask);

		// Assert
		Assert.That(names, Is.EqualTo(new object[] { new MicroTaskDescription.Custom("download 2.2.0"), new MicroTaskDescription.Custom("extract 2.2.0"), new MicroTaskDescription.Custom("steps 2.2.0"), MicroTaskDescription.UpdateModMetadata.Instance }));
	}

	[Test]
	public async Task CreateModInstallAppTask_InstalledPatchOfOlderVersion_RollsBackAppliedPatchesNewestFirstThenOlderVersion()
	{
		// Arrange
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		var prerequisites = mocker.GetMock<IPrerequisitesAppTaskProvider>();
		prerequisites.SetupGet(x => x.InstallVcRedistTasks).Returns([]);
		prerequisites.Setup(x => x.CreateInstallSevenZipTask(CancellationToken.None))
			.ReturnsAsync(new AppTask("InstallSevenZip", new AppTaskDescription.Custom("7-Zip"), _ => [], [], null));
		mocker.GetMock<IFileStorageService>().Setup(x => x.GetFilePath("mod-2.0.0.7z")).Returns(@"C:\Downloads\mod-2.0.0.7z");
		var modOrganizer = mocker.GetMock<IModOrganizerService>();
		modOrganizer.Setup(x => x.IsModInstalled("Mod")).Returns(true);
		modOrganizer.SetupGet(x => x.InstalledMods).Returns(
			[new ModInfo("Mod", new Lazy<MetaIniData?>(() => new MetaIniData { Comments = null, Version = "1.2.0" }))]);
		modOrganizer.Setup(x => x.UninstallMod("Mod", CancellationToken.None)).Returns(Task.CompletedTask);
		modOrganizer.Setup(x => x.CreateOrUpdateMetaIniFile("Mod", @"C:\Downloads\mod-2.0.0.7z", NuGetVersion.Parse("2.0.0"),
				"Mod", CancellationToken.None))
			.Returns(Task.CompletedTask);
		var mod = CreateMod([new InstallStep { Actions = [], CanRollback = true }], [new InstallStep { Actions = [] }],
			v1Patches: [CreatePatch("1.3.0"), CreatePatch("1.1.0"), CreatePatch("1.2.0")]);
		var factory = mocker.GetMock<IMicroTasksFactory>();
		factory.Setup(x => x.CreateDownloadFileMicroTask(It.Is<DownloadFileSource>(source => ReferenceEquals(source.Value, mod.Items[1].ArchiveDownloadInfo))))
			.Returns(Marker("download 2.0.0"));
		factory.Setup(x => x.CreateInstallModMicroTask("Mod", "mod-2.0.0.7z", new ModPlacement.UnderSeparator("Extras"))).Returns(Marker("install 2.0.0"));
		var installSteps = UseInstallStepsMicroTasksFactory(mocker);
		installSteps.Setup(x => x.CreateRollbackMicroTasks(mod.Items[0].InstallSteps!, BackupId("1.0.0"))).Returns([Marker("rollback 1.0.0")]);
		// 1.3.0 is not applied, so the strict mock fails if its steps are rolled back.
		installSteps.Setup(x => x.CreateRollbackMicroTasks(mod.Items[0].Patches![1].InstallSteps!, BackupId("1.1.0"))).Returns([Marker("rollback 1.1.0")]);
		installSteps.Setup(x => x.CreateRollbackMicroTasks(mod.Items[0].Patches![2].InstallSteps!, BackupId("1.2.0"))).Returns([Marker("rollback 1.2.0")]);
		installSteps.Setup(x => x.CreateInstallMicroTasks(mod.Items[1].InstallSteps!, BackupId("2.0.0"))).Returns([Marker("steps 2.0.0")]);
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();
		var appTask = await sut.CreateModInstallAppTask(mod, NuGetVersion.Parse("2.0.0"), CancellationToken.None);

		// Act
		var names = await RunMicroTasks(mocker, appTask);

		// Assert
		Assert.That(names, Is.EqualTo(new object[]
		{
			new MicroTaskDescription.Custom("rollback 1.2.0"), new MicroTaskDescription.Custom("rollback 1.1.0"), new MicroTaskDescription.Custom("rollback 1.0.0"), MicroTaskDescription.DeletePreviousVersion.Instance, MicroTaskDescription.DeleteBackups.Instance,
			new MicroTaskDescription.Custom("download 2.0.0"), MicroTaskDescription.PreparingInstallation.Instance, new MicroTaskDescription.Custom("install 2.0.0"), new MicroTaskDescription.Custom("steps 2.0.0"),
			MicroTaskDescription.UpdateModMetadata.Instance, MicroTaskDescription.FinishInstallation.Instance,
		}));
	}

	[Test]
	public async Task CreateModInstallAppTask_UnfinishedInstallation_RollsBackUnfinishedVersionThenReinstallsIt()
	{
		// Arrange: 2.0.0 was interrupted before meta.ini got its version, so meta.ini still says 1.0.0.
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		var prerequisites = mocker.GetMock<IPrerequisitesAppTaskProvider>();
		prerequisites.SetupGet(x => x.InstallVcRedistTasks).Returns([]);
		prerequisites.Setup(x => x.CreateInstallSevenZipTask(CancellationToken.None))
			.ReturnsAsync(new AppTask("InstallSevenZip", new AppTaskDescription.Custom("7-Zip"), _ => [], [], null));
		mocker.GetMock<IFileStorageService>().Setup(x => x.GetFilePath("mod-2.0.0.7z")).Returns(@"C:\Downloads\mod-2.0.0.7z");
		var modOrganizer = mocker.GetMock<IModOrganizerService>();
		modOrganizer.Setup(x => x.IsModInstalled("Mod")).Returns(true);
		modOrganizer.SetupGet(x => x.InstalledMods).Returns(
			[new ModInfo("Mod", new Lazy<MetaIniData?>(() => new MetaIniData { Comments = null, Version = "1.0.0" }))]);
		modOrganizer.Setup(x => x.UninstallMod("Mod", CancellationToken.None)).Returns(Task.CompletedTask);
		modOrganizer.Setup(x => x.CreateOrUpdateMetaIniFile("Mod", @"C:\Downloads\mod-2.0.0.7z", NuGetVersion.Parse("2.0.0"),
				"Mod", CancellationToken.None))
			.Returns(Task.CompletedTask);
		// Its patch must not be applied alone: the steps of 2.0.0 itself did not finish.
		var mod = CreateMod([new InstallStep { Actions = [], CanRollback = true }],
			[new InstallStep { Actions = [], CanRollback = true }], v2Patches: [CreatePatch("2.1.0")]);
		var factory = mocker.GetMock<IMicroTasksFactory>();
		factory.Setup(x => x.CreateDownloadFileMicroTask(It.Is<DownloadFileSource>(source => ReferenceEquals(source.Value, mod.Items[1].ArchiveDownloadInfo))))
			.Returns(Marker("download 2.0.0"));
		factory.Setup(x => x.CreateInstallModMicroTask("Mod", "mod-2.0.0.7z", new ModPlacement.UnderSeparator("Extras"))).Returns(Marker("install 2.0.0"));
		var installSteps = UseInstallStepsMicroTasksFactory(mocker);
		SetupUnfinishedInstallation(mocker, "2.0.0");
		// 1.0.0 is not installed, so the strict mock fails if its steps are rolled back.
		installSteps.Setup(x => x.CreateRollbackMicroTasks(mod.Items[1].InstallSteps!, BackupId("2.0.0"))).Returns([Marker("rollback 2.0.0")]);
		installSteps.Setup(x => x.CreateInstallMicroTasks(mod.Items[1].InstallSteps!, BackupId("2.0.0"))).Returns([Marker("steps 2.0.0")]);
		SetupPatches(mocker, installSteps, mod.Items[1].Patches!);
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();
		var appTask = await sut.CreateModInstallAppTask(mod, NuGetVersion.Parse("2.0.0"), CancellationToken.None);

		// Act
		var names = await RunMicroTasks(mocker, appTask);

		// Assert
		Assert.That(names, Is.EqualTo(new object[]
		{
			new MicroTaskDescription.Custom("rollback 2.0.0"), MicroTaskDescription.DeletePreviousVersion.Instance, MicroTaskDescription.DeleteBackups.Instance,
			new MicroTaskDescription.Custom("download 2.0.0"), MicroTaskDescription.PreparingInstallation.Instance, new MicroTaskDescription.Custom("install 2.0.0"), new MicroTaskDescription.Custom("steps 2.0.0"), MicroTaskDescription.UpdateModMetadata.Instance,
			MicroTaskDescription.FinishInstallation.Instance,
			new MicroTaskDescription.Custom("download 2.1.0"), new MicroTaskDescription.Custom("extract 2.1.0"), new MicroTaskDescription.Custom("steps 2.1.0"), MicroTaskDescription.UpdateModMetadata.Instance,
		}));
	}

	[Test]
	public async Task CreateModUninstallAppTask_FeedVersion_RollsBackStepsThenUninstallsMod()
	{
		// Arrange
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		mocker.Use<ILogger<InstallAppTaskProvider>>(NullLogger<InstallAppTaskProvider>.Instance);
		var modOrganizer = mocker.GetMock<IModOrganizerService>();
		modOrganizer.Setup(x => x.IsModInstalled("Mod")).Returns(true);
		modOrganizer.SetupGet(x => x.InstalledMods).Returns(
			[new ModInfo("Mod", new Lazy<MetaIniData?>(() => new MetaIniData { Comments = null, Version = "1.0.0" }))]);
		modOrganizer.Setup(x => x.UninstallMod("Mod", CancellationToken.None)).Returns(Task.CompletedTask);
		var mod = CreateMod([new InstallStep { Actions = [], CanRollback = true }], [], v1SupportsUninstall: true);
		UseInstallStepsMicroTasksFactory(mocker)
			.Setup(x => x.CreateRollbackMicroTasks(mod.Items[0].InstallSteps!, BackupId("1.0.0")))
			.Returns([Marker("rollback 1.0.0")]);
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();

		// Act
		var names = await RunMicroTasks(mocker, sut.CreateModUninstallAppTask(mod));

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(names, Is.EqualTo(new object[] { new MicroTaskDescription.Custom("rollback 1.0.0"), new MicroTaskDescription.UninstallMod("Mod"), MicroTaskDescription.DeleteBackups.Instance }));
			modOrganizer.Verify(x => x.UninstallMod("Mod", CancellationToken.None), Times.Once);
		});
	}

	[Test]
	public async Task CreateModUninstallAppTask_VersionNotInFeed_OnlyRemovesMod()
	{
		// Arrange
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		mocker.Use<ILogger<InstallAppTaskProvider>>(NullLogger<InstallAppTaskProvider>.Instance);
		var modOrganizer = mocker.GetMock<IModOrganizerService>();
		modOrganizer.Setup(x => x.IsModInstalled("Mod")).Returns(true);
		modOrganizer.SetupGet(x => x.InstalledMods).Returns(
			[new ModInfo("Mod", new Lazy<MetaIniData?>(() => new MetaIniData { Comments = null, Version = "1.5.0" }))]);
		modOrganizer.Setup(x => x.UninstallMod("Mod", CancellationToken.None)).Returns(Task.CompletedTask);
		var mod = CreateMod(
			[new InstallStep { Actions = [new InstallAction.ChangeProfile("Graph pack")], CanRollback = true }], []);
		SetupUnfinishedInstallation(mocker, null);
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();

		// Act
		await RunMicroTasks(mocker, sut.CreateModUninstallAppTask(mod));

		// Assert
		modOrganizer.Verify(x => x.UninstallMod("Mod", CancellationToken.None), Times.Once);
	}

	[Test]
	public async Task CreateModUninstallAppTask_UnfinishedInstallation_RollsBackUnfinishedVersion()
	{
		// Arrange: meta.ini of the interrupted 1.0.0 installation has the archive's own version, which is not listed.
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		var modOrganizer = mocker.GetMock<IModOrganizerService>();
		modOrganizer.Setup(x => x.IsModInstalled("Mod")).Returns(true);
		modOrganizer.SetupGet(x => x.InstalledMods).Returns(
			[new ModInfo("Mod", new Lazy<MetaIniData?>(() => new MetaIniData { Comments = null, Version = "1.5.0" }))]);
		modOrganizer.Setup(x => x.UninstallMod("Mod", CancellationToken.None)).Returns(Task.CompletedTask);
		var mod = CreateMod([new InstallStep { Actions = [], CanRollback = true }], [], v1SupportsUninstall: true);
		UseInstallStepsMicroTasksFactory(mocker)
			.Setup(x => x.CreateRollbackMicroTasks(mod.Items[0].InstallSteps!, BackupId("1.0.0")))
			.Returns([Marker("rollback 1.0.0")]);
		SetupUnfinishedInstallation(mocker, "1.0.0");
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();

		// Act
		var names = await RunMicroTasks(mocker, sut.CreateModUninstallAppTask(mod));

		// Assert
		Assert.That(names, Is.EqualTo(new object[] { new MicroTaskDescription.Custom("rollback 1.0.0"), new MicroTaskDescription.UninstallMod("Mod"), MicroTaskDescription.DeleteBackups.Instance }));
	}

	[Test]
	public void CreateModUninstallAppTask_FeedVersionWithoutUninstallSupport_Throws()
	{
		// Arrange
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		mocker.Use<ILogger<InstallAppTaskProvider>>(NullLogger<InstallAppTaskProvider>.Instance);
		var modOrganizer = mocker.GetMock<IModOrganizerService>();
		modOrganizer.Setup(x => x.IsModInstalled("Mod")).Returns(true);
		modOrganizer.SetupGet(x => x.InstalledMods).Returns(
			[new ModInfo("Mod", new Lazy<MetaIniData?>(() => new MetaIniData { Comments = null, Version = "1.0.0" }))]);
		var mod = CreateMod([new InstallStep { Actions = [new InstallAction.ChangeProfile("Graph pack")], CanRollback = true }], []);
		SetupUnfinishedInstallation(mocker, null);
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();
		var appTask = sut.CreateModUninstallAppTask(mod);

		// Act
		void Act() => appTask.MicroTasksProvider(CreateMicroTasksProviderContext(mocker));

		// Assert
		Assert.That(Act, Throws.InvalidOperationException);
	}

	[TestCase("1.0.0", "3.0.0", false, true, false)]
	[TestCase("1.0.0", "3.0.0", true, true, true)]
	[TestCase("2.0.0", "3.0.0", true, false, false)]
	[TestCase("1.5.0", "3.0.0", false, false, true)]
	[TestCase("1.0.0", "2.0.0", true, false, true)]
	[TestCase("1.0.0", null, true, false, true)]
	public void GetModInstallState_InstalledMod_OffersUpdateFromFeedVersionAndUninstallWhenSupportedOrUnlisted(
		string installedVersion, string? modpackVersion, bool v1SupportsUninstall, bool expectedUpdate, bool expectedUninstall)
	{
		// Arrange
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		var modOrganizer = mocker.GetMock<IModOrganizerService>();
		modOrganizer.Setup(x => x.IsModInstalled("Mod")).Returns(true);
		modOrganizer.SetupGet(x => x.InstalledMods).Returns(
		[
			new ModInfo("Other", new Lazy<MetaIniData?>(() => null)),
			new ModInfo("Mod", new Lazy<MetaIniData?>(() => new MetaIniData { Comments = null, Version = installedVersion })),
		]);
		SetupUnfinishedInstallation(mocker, null);
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();

		// Act
		var state = sut.GetModInstallState(CreateMod([], [], v1SupportsUninstall),
			modpackVersion == null ? null : NuGetVersion.Parse(modpackVersion));

		// Assert
		Assert.That(state, Is.EqualTo(ModInstallState.Installed(expectedUpdate, expectedUninstall)));
	}

	[TestCase("2.0.0", true)]
	[TestCase("2.1.0", true)]
	[TestCase("2.2.0", false)]
	public void GetModInstallState_InstalledVersionWithPatches_OffersUpdateWhilePatchesAreMissing(string installedVersion,
		bool expectedUpdate)
	{
		// Arrange
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		var modOrganizer = mocker.GetMock<IModOrganizerService>();
		modOrganizer.Setup(x => x.IsModInstalled("Mod")).Returns(true);
		modOrganizer.SetupGet(x => x.InstalledMods).Returns(
			[new ModInfo("Mod", new Lazy<MetaIniData?>(() => new MetaIniData { Comments = null, Version = installedVersion }))]);
		SetupUnfinishedInstallation(mocker, null);
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();

		// Act
		var state = sut.GetModInstallState(CreateMod([], [], v2Patches: [CreatePatch("2.1.0"), CreatePatch("2.2.0")]),
			NuGetVersion.Parse("3.0.0"));

		// Assert: a patch version is found through its mod version, which never supports uninstalling.
		Assert.That(state, Is.EqualTo(ModInstallState.Installed(expectedUpdate, isUninstallSupported: false)));
	}

	[Test]
	public void GetModInstallState_ModNotInstalled_ReturnsNotInstalled()
	{
		// Arrange
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		mocker.GetMock<IModOrganizerService>().Setup(x => x.IsModInstalled("Mod")).Returns(false);
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();

		// Act
		var state = sut.GetModInstallState(CreateMod(), NuGetVersion.Parse("3.0.0"));

		// Assert
		Assert.That(state, Is.EqualTo(ModInstallState.NotInstalled));
	}

	/// <summary>Builds the task's microtasks, runs them in order, and returns their descriptions.</summary>
	private static async Task<object[]> RunMicroTasks(AutoMocker mocker, AppTask appTask)
	{
		mocker.GetMock<IMicroTasksFactory>()
			.Setup(x => x.CreateDelegateMicroTask(
				It.IsAny<Func<ISubject<MicroTaskStep>, CancellationToken, ValueTask>>(), It.IsAny<MicroTaskDescription>()))
			.Returns<Func<ISubject<MicroTaskStep>, CancellationToken, ValueTask>, MicroTaskDescription>(
				(func, description) => new DelegateMicroTask(description, func));
		using var subject = new Subject<MicroTaskStep>();

		var microTasks = appTask.MicroTasksProvider(CreateMicroTasksProviderContext(mocker)).ToArray();
		foreach (var microTask in microTasks)
		{
			await microTask.Execute(subject, CancellationToken.None);
		}

		return microTasks.Select(x => x.Description.Value).ToArray();
	}

	private static MicroTasksProviderContext CreateMicroTasksProviderContext(AutoMocker mocker) =>
		new(mocker.GetMock<IMicroTasksFactory>().Object, mocker.GetMock<IServiceProvider>().Object);

	/// <summary>
	/// Makes the task's service provider resolve the returned steps factory mock and a backup service that can delete the
	/// backups of the mod <c>Mod</c> and mark its installations, none of which is unfinished.
	/// </summary>
	private static Mock<IInstallStepsMicroTasksFactory> UseInstallStepsMicroTasksFactory(AutoMocker mocker)
	{
		var installSteps = mocker.GetMock<IInstallStepsMicroTasksFactory>();
		var backups = mocker.GetMock<IInstallBackupService>();
		backups.Setup(x => x.Delete(InstallBackupId.ForMod("Mod")));
		backups.Setup(x => x.StartInstallation(It.IsAny<InstallBackupId>(), CancellationToken.None)).Returns(Task.CompletedTask);
		backups.Setup(x => x.FinishInstallation(It.IsAny<InstallBackupId>()));
		SetupUnfinishedInstallation(mocker, null);
		var serviceProvider = mocker.GetMock<IServiceProvider>();
		serviceProvider.Setup(x => x.GetService(typeof(IInstallStepsMicroTasksFactory))).Returns(installSteps.Object);
		serviceProvider.Setup(x => x.GetService(typeof(IInstallBackupService))).Returns(backups.Object);
		return installSteps;
	}

	/// <summary>Makes the installation of the version <paramref name="version"/> of <c>Mod</c>, if any, unfinished.</summary>
	private static void SetupUnfinishedInstallation(AutoMocker mocker, string? version) =>
		mocker.GetMock<IInstallBackupService>()
			.Setup(x => x.IsInstallationUnfinished(It.IsAny<InstallBackupId>()))
			.Returns((InstallBackupId id) => version != null && id == BackupId(version));

	private static InstallBackupId BackupId(string version) =>
		InstallBackupId.ForModVersion("Mod", NuGetVersion.Parse(version));

	/// <summary>A microtask that does nothing, told apart by its name.</summary>
	private static IAppMicroTask Marker(string name) => new DelegateMicroTask(new MicroTaskDescription.Custom(name), (_, _) => ValueTask.CompletedTask);

	private static ModFeedData CreateMod() => CreateMod([], []);

	/// <summary>
	/// Version 1.0.0 (with <paramref name="v1Patches"/>) supports modpack 1.0.0 and later, version 2.0.0 (never
	/// uninstallable, with <paramref name="v2Patches"/>) modpack 3.0.0 and later.
	/// </summary>
	private static ModFeedData CreateMod(IReadOnlyList<InstallStep> v1Steps, IReadOnlyList<InstallStep> v2Steps,
		bool v1SupportsUninstall = false, IReadOnlyList<ModPatchFeedItem>? v1Patches = null,
		IReadOnlyList<ModPatchFeedItem>? v2Patches = null) => new()
	{
		Name = "Mod",
		LogoPath = "mod.png",
		DescriptionPath = new() { ["ru"] = "mod.md" },
		Items =
		[
			new ModFeedItem
			{
				Version = NuGetVersion.Parse("1.0.0"),
				ArchiveDownloadInfo = new DownloadInfo { S3Path = "Mods/mod-1.0.0.7z" },
				InstallSteps = v1Steps,
				Patches = v1Patches,
				SupportedModpackVersion = VersionRange.Parse("[1.0.0,)"),
				SupportsUninstall = v1SupportsUninstall,
			},
			new ModFeedItem
			{
				Version = NuGetVersion.Parse("2.0.0"),
				ArchiveDownloadInfo = new DownloadInfo { S3Path = "Mods/mod-2.0.0.7z" },
				Placement = new ModPlacement.UnderSeparator("Extras"),
				InstallSteps = v2Steps,
				Patches = v2Patches,
				SupportedModpackVersion = VersionRange.Parse("[3.0.0,)"),
			},
		],
	};

	private static ModPatchFeedItem CreatePatch(string version) => new()
	{
		Version = NuGetVersion.Parse(version),
		DownloadInfo = new DownloadInfo { S3Path = $"Mods/mod-{version}.7z" },
		InstallSteps = [new InstallStep { Actions = [] }],
	};

	/// <summary>
	/// Sets up the microtasks of <paramref name="patches"/> and the meta.ini update to each patch version, installed from
	/// the 2.0.0 archive.
	/// </summary>
	private static void SetupPatches(AutoMocker mocker, Mock<IInstallStepsMicroTasksFactory> installSteps,
		IEnumerable<ModPatchFeedItem> patches)
	{
		var factory = mocker.GetMock<IMicroTasksFactory>();
		foreach (var patch in patches)
		{
			var version = patch.Version.ToNormalizedString();
			factory.Setup(x => x.CreateDownloadFileMicroTask(It.Is<DownloadFileSource>(source => ReferenceEquals(source.Value, patch.DownloadInfo))))
				.Returns(Marker($"download {version}"));
			MicroTaskDescription? extractPatch = new MicroTaskDescription.ExtractModPatch(patch.Version);
			factory.Setup(x => x.CreateExtractSevenZipMicroTask($"mod-{version}.7z",
					It.Is<SkyrimRelativePath>(path => path.Path == @"Example MO\mods\Mod"), extractPatch))
				.Returns(Marker($"extract {version}"));
			installSteps.Setup(x => x.CreateInstallMicroTasks(patch.InstallSteps!, BackupId(version))).Returns([Marker($"steps {version}")]);
			mocker.GetMock<IModOrganizerService>()
				.Setup(x => x.CreateOrUpdateMetaIniFile("Mod", @"C:\Downloads\mod-2.0.0.7z", patch.Version, "Mod",
					CancellationToken.None))
				.Returns(Task.CompletedTask);
		}
	}
}
