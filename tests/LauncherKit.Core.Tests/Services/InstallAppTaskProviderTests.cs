using LauncherKit.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Versioning;
using R3;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.AppTasks.InstallSteps;
using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.Core.Backups;
using LauncherKit.Core.Choices;
using LauncherKit.Core.Exceptions;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.Install;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.Models;
using LauncherKit.Core.ModDependencies;
using LauncherKit.Core.Services;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Tests.TestSupport;
using LauncherKit.TestSupport;

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
		installSteps.Setup(x => x.CreateRollbackMicroTasks(mod.Items[0].InstallSteps!, BackupId("1.0.0"), It.IsAny<Func<string, CancellationToken, ValueTask<bool>>>())).Returns([Marker("rollback 1.0.0")]);
		installSteps.Setup(x => x.CreateInstallMicroTasks(mod.Items[1].InstallSteps!, BackupId("2.0.0"), It.IsAny<IDictionary<string, NuGetVersion>>())).Returns([Marker("steps 2.0.0")]);
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
		installSteps.Setup(x => x.CreateInstallMicroTasks(mod.Items[1].InstallSteps!, BackupId("2.0.0"), It.IsAny<IDictionary<string, NuGetVersion>>())).Returns([Marker("steps 2.0.0")]);
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
		installSteps.Setup(x => x.CreateRollbackMicroTasks(mod.Items[0].InstallSteps!, BackupId("1.0.0"), It.IsAny<Func<string, CancellationToken, ValueTask<bool>>>())).Returns([Marker("rollback 1.0.0")]);
		// 1.3.0 is not applied, so the strict mock fails if its steps are rolled back.
		installSteps.Setup(x => x.CreateRollbackMicroTasks(mod.Items[0].Patches![1].InstallSteps!, BackupId("1.1.0"), It.IsAny<Func<string, CancellationToken, ValueTask<bool>>>())).Returns([Marker("rollback 1.1.0")]);
		installSteps.Setup(x => x.CreateRollbackMicroTasks(mod.Items[0].Patches![2].InstallSteps!, BackupId("1.2.0"), It.IsAny<Func<string, CancellationToken, ValueTask<bool>>>())).Returns([Marker("rollback 1.2.0")]);
		installSteps.Setup(x => x.CreateInstallMicroTasks(mod.Items[1].InstallSteps!, BackupId("2.0.0"), It.IsAny<IDictionary<string, NuGetVersion>>())).Returns([Marker("steps 2.0.0")]);
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
		installSteps.Setup(x => x.CreateRollbackMicroTasks(mod.Items[1].InstallSteps!, BackupId("2.0.0"), It.IsAny<Func<string, CancellationToken, ValueTask<bool>>>())).Returns([Marker("rollback 2.0.0")]);
		installSteps.Setup(x => x.CreateInstallMicroTasks(mod.Items[1].InstallSteps!, BackupId("2.0.0"), It.IsAny<IDictionary<string, NuGetVersion>>())).Returns([Marker("steps 2.0.0")]);
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
			.Setup(x => x.CreateRollbackMicroTasks(mod.Items[0].InstallSteps!, BackupId("1.0.0"), It.IsAny<Func<string, CancellationToken, ValueTask<bool>>>()))
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
			.Setup(x => x.CreateRollbackMicroTasks(mod.Items[0].InstallSteps!, BackupId("1.0.0"), It.IsAny<Func<string, CancellationToken, ValueTask<bool>>>()))
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

	[Test]
	public async Task CreateModInstallWithDependenciesAppTask_ModWithoutDependencies_CreatesThePlainInstallTask()
	{
		// Arrange
		var mocker = CreateDependencyMocker();
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();

		// Act
		var appTask = await sut.CreateModInstallWithDependenciesAppTask(CreateMod(), null, Reservations(_ => { }),
			CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(appTask.Id, Is.EqualTo("Install_Mod"));
			Assert.That(appTask.Dependencies.Select(x => x.Id), Is.EqualTo(new[] { "InstallSevenZip" }));
			Assert.That((object?)appTask.IsUnneeded, Is.Null);
		});
	}

	[Test]
	public async Task CreateModInstallWithDependenciesAppTask_DependencyNotInstalled_InstallsItBeforeTheMod()
	{
		// Arrange
		var mocker = CreateDependencyMocker();
		var mod = CreateMod();
		var addon = CreateAddon(new ModDependencyAlternative("Mod", VersionRange.Parse("[2.0.0, )")));
		SetupFeed(mocker, mod, addon);
		var modOrganizer = mocker.GetMock<IModOrganizerService>();
		modOrganizer.Setup(x => x.IsModInstalled(It.IsAny<string>())).Returns(false);
		modOrganizer.Setup(x => x.CreateOrUpdateMetaIniFile(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<NuGetVersion>(),
			It.IsAny<string>(), CancellationToken.None)).Returns(Task.CompletedTask);
		mocker.GetMock<IFileStorageService>().Setup(x => x.GetFilePath(It.IsAny<string>())).Returns(@"C:\Downloads\file.7z");
		var factory = mocker.GetMock<IMicroTasksFactory>();
		factory.Setup(x => x.CreateDownloadFileMicroTask(It.Is<DownloadFileSource>(source => ReferenceEquals(source.Value, mod.Items[1].ArchiveDownloadInfo))))
			.Returns(Marker("download Mod"));
		factory.Setup(x => x.CreateDownloadFileMicroTask(It.Is<DownloadFileSource>(source => ReferenceEquals(source.Value, addon.Items[0].ArchiveDownloadInfo))))
			.Returns(Marker("download Addon"));
		factory.Setup(x => x.CreateInstallModMicroTask("Mod", "mod-2.0.0.7z", It.IsAny<ModPlacement?>())).Returns(Marker("install Mod"));
		factory.Setup(x => x.CreateInstallModMicroTask("Addon", "addon-1.0.0.7z", null)).Returns(Marker("install Addon"));
		var installSteps = UseInstallStepsMicroTasksFactory(mocker);
		installSteps.Setup(x => x.CreateInstallMicroTasks(It.IsAny<IReadOnlyList<InstallStep>>(), It.IsAny<InstallBackupId?>(), It.IsAny<IDictionary<string, NuGetVersion>>())).Returns([]);
		mocker.GetMock<IInstallBackupService>().Setup(x => x.Delete(InstallBackupId.ForMod("Addon")));
		var reserved = new List<string>();
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();
		var appTask = await sut.CreateModInstallWithDependenciesAppTask(addon, null,
			Reservations(mods => reserved.AddRange(mods.Select(x => x.Name))), CancellationToken.None);
		var resolveTask = appTask.Dependencies.Single();

		// Act
		await RunMicroTasks(mocker, resolveTask);
		var names = await RunMicroTasks(mocker, appTask);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(resolveTask.Dependencies.Select(x => x.Id), Is.EqualTo(new[] { "InstallSevenZip" }));
			Assert.That(reserved, Is.EqualTo(new[] { "Mod" }));
			Assert.That(appTask.IsUnneeded!(), Is.False);
			Assert.That(names.OfType<MicroTaskDescription.Custom>().Select(x => x.Text),
				Is.EqualTo(new[] { "download Mod", "install Mod", "download Addon", "install Addon" }));
		});
	}

	[Test]
	public async Task CreateModInstallWithDependenciesAppTask_UpgradeDeclined_ReservesNothingAndSkipsTheInstallation()
	{
		// Arrange
		var mocker = CreateDependencyMocker();
		var mod = CreateMod();
		var addon = CreateAddon(new ModDependencyAlternative("Mod", VersionRange.Parse("[2.0.0, )")));
		SetupFeed(mocker, mod, addon);
		var modOrganizer = mocker.GetMock<IModOrganizerService>();
		modOrganizer.Setup(x => x.IsModInstalled("Mod")).Returns(true);
		modOrganizer.Setup(x => x.IsModInstalled("Addon")).Returns(false);
		modOrganizer.SetupGet(x => x.InstalledMods).Returns(
			[new ModInfo("Mod", new Lazy<MetaIniData?>(() => new MetaIniData { Comments = null, Version = "1.0.0" }))]);
		SetupUnfinishedInstallation(mocker, null);
		var reserved = false;
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();
		var appTask = await sut.CreateModInstallWithDependenciesAppTask(addon, null, Reservations(_ => reserved = true),
			CancellationToken.None);
		IReadOnlyList<ModUpgrade>? upgrades = null;

		// Act
		await RunMicroTasks(mocker, appTask.Dependencies.Single(), step =>
		{
			if (step is MicroTaskStep.ConfirmModUpgrades(var confirmation))
			{
				upgrades = confirmation.Upgrades;
				confirmation.SetResult(false);
			}
		});

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(upgrades, Is.EqualTo(new[] { new ModUpgrade(mod, NuGetVersion.Parse("1.0.0"), NuGetVersion.Parse("2.0.0")) }));
			Assert.That(reserved, Is.False);
			Assert.That(appTask.IsUnneeded!(), Is.True);
		});
	}

	[Test]
	public async Task CreateModInstallWithDependenciesAppTask_SeveralAlternatives_AsksWithUnavailableOptionDisabled()
	{
		// Arrange
		var mocker = CreateDependencyMocker();
		var mod = CreateMod();
		var library = new ModFeedData
		{
			Name = "Library", CategoryId = "mods", LogoPath = "library.png", DescriptionPath = new() { ["ru"] = "library.md" },
			Items = CreateAddon().Items,
		};
		var addon = CreateAddon(new ModDependencyAlternative("Mod", VersionRange.Parse("[5.0.0, )")),
			new ModDependencyAlternative("Library", VersionRange.Parse("[1.0.0, )")));
		SetupFeed(mocker, mod, library, addon);
		mocker.GetMock<IModOrganizerService>().Setup(x => x.IsModInstalled(It.IsAny<string>())).Returns(false);
		var reserved = new List<string>();
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();
		var appTask = await sut.CreateModInstallWithDependenciesAppTask(addon, null,
			Reservations(mods => reserved.AddRange(mods.Select(x => x.Name))), CancellationToken.None);
		IReadOnlyList<ChoiceOption>? options = null;

		// Act
		await RunMicroTasks(mocker, appTask.Dependencies.Single(), step =>
		{
			if (step is MicroTaskStep.SelectChoices(var context))
			{
				var choiceStep = context.Steps.Single();
				options = choiceStep.Options;
				context.SetResult([new ChoiceStepSelection { Step = choiceStep, SelectedOptions = [options[1]] }]);
			}
		});

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(options!.Select(x => x.IsAvailable), Is.EqualTo(new[] { false, true }));
			Assert.That(options!.Select(x => x.IsSelectedByDefault), Is.EqualTo(new[] { false, true }));
			Assert.That(reserved, Is.EqualTo(new[] { "Library" }));
		});
	}

	[Test]
	public async Task CreateModInstallWithDependenciesAppTask_RequestEndsWhileChoosing_EndsResolutionWithoutReservingAndSkipsTheInstallation()
	{
		// Arrange
		var mocker = CreateDependencyMocker();
		var library = CreateLibrary();
		var addon = CreateAddon(new ModDependencyAlternative("Mod", VersionRange.Parse("[2.0.0, )")),
			new ModDependencyAlternative("Library", VersionRange.Parse("[1.0.0, )")));
		SetupFeed(mocker, CreateMod(), library, addon);
		mocker.GetMock<IModOrganizerService>().Setup(x => x.IsModInstalled(It.IsAny<string>())).Returns(false);
		using var requestEnded = new CancellationTokenSource();
		var reserved = false;
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();
		var appTask = await sut.CreateModInstallWithDependenciesAppTask(addon, null, Reservations(_ => reserved = true,
			requestEnded.Token), CancellationToken.None);
		var asked = false;

		// Act
		await RunMicroTasks(mocker, appTask.Dependencies.Single(), step =>
		{
			if (step is MicroTaskStep.SelectChoices)
			{
				asked = true;
				requestEnded.Cancel();
			}
		});

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(asked, Is.True);
			Assert.That(reserved, Is.False);
			Assert.That(appTask.IsUnneeded!(), Is.True);
		});
	}

	[Test]
	public async Task CreateModInstallWithDependenciesAppTask_ExclusiveConflictQueuedMeanwhile_FailsWithoutPlanning()
	{
		// Arrange
		var mocker = CreateDependencyMocker();
		var library = CreateLibrary();
		var otherLibrary = CreateLibrary("Other Library");
		var addon = CreateAddon(new ModDependencyAlternative("Library", VersionRange.Parse("[1.0.0, )")));
		SetupFeed(mocker, library, otherLibrary, addon);
		mocker.GetMock<IModOrganizerService>().Setup(x => x.IsModInstalled(It.IsAny<string>())).Returns(false);
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();
		var reservations = new ModDependencyReservations(() => [], (mods, _) => new ModReservationConflict(mods.Single(), otherLibrary),
			CancellationToken.None);
		var appTask = await sut.CreateModInstallWithDependenciesAppTask(addon, null, reservations, CancellationToken.None);

		// Act
		var exception = await Assert.ThrowsAsync<ModDependencyUnavailableException>(() => RunMicroTasks(mocker, appTask.Dependencies.Single()));

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(exception!.Alternatives.Single().UnavailableReason,
				Is.EqualTo((ModDependencyUnavailableReason)new ModDependencyUnavailableReason.OtherModInExclusiveCategory(otherLibrary)));
			Assert.That(() => appTask.MicroTasksProvider(CreateMicroTasksProviderContext(mocker)), Throws.InvalidOperationException);
		});
	}

	[TestCase(true)]
	[TestCase(false)]
	public async Task CreateModInstallWithDependenciesAppTask_FirstTimeDependencyRequiringNewGame_AsksToConfirmIt(bool accept)
	{
		// Arrange
		var mocker = CreateDependencyMocker();
		var library = CreateLibrary(requiresNewGame: true);
		var addon = CreateAddon(new ModDependencyAlternative("Library", VersionRange.Parse("[1.0.0, )")));
		SetupFeed(mocker, library, addon);
		mocker.GetMock<IModOrganizerService>().Setup(x => x.IsModInstalled(It.IsAny<string>())).Returns(false);
		var reserved = false;
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();
		var appTask = await sut.CreateModInstallWithDependenciesAppTask(addon, null, Reservations(_ => reserved = true),
			CancellationToken.None);
		ModUpgradesConfirmation? asked = null;

		// Act
		await RunMicroTasks(mocker, appTask.Dependencies.Single(), step =>
		{
			if (step is MicroTaskStep.ConfirmModUpgrades(var confirmation))
			{
				asked = confirmation;
				confirmation.SetResult(accept);
			}
		});

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(asked!.Upgrades, Is.Empty);
			Assert.That(asked.NewGameInstalls, Is.EqualTo(new[] { library }));
			Assert.That(reserved, Is.EqualTo(accept));
			Assert.That(appTask.IsUnneeded!(), Is.EqualTo(!accept));
		});
	}

	[Test]
	public async Task CreateModInstallWithDependenciesAppTask_OldVersionInstalledTheDependency_RollbackKeepsItAndNewVersionUsesIt()
	{
		// Arrange
		var mocker = CreateDependencyMocker();
		var library = CreateLibrary();
		var addon = AddonWith(
			new ModFeedItem
			{
				Version = NuGetVersion.Parse("1.0.0"),
				ArchiveDownloadInfo = new DownloadInfo { S3Path = "Mods/addon-1.0.0.7z" },
				SupportedModpackVersion = VersionRange.Parse("[1.0.0,)"),
				InstallSteps = [InstallLibraryStep()],
			},
			new ModFeedItem
			{
				Version = NuGetVersion.Parse("2.0.0"),
				ArchiveDownloadInfo = new DownloadInfo { S3Path = "Mods/addon-2.0.0.7z" },
				SupportedModpackVersion = VersionRange.Parse("[1.0.0,)"),
				Dependencies = [new ModDependency.AnotherMod([new ModDependencyAlternative("Library", VersionRange.Parse("[1.0.0, )"))])],
			});
		SetupFeed(mocker, library, addon);
		var modOrganizer = mocker.GetMock<IModOrganizerService>();
		modOrganizer.Setup(x => x.IsModInstalled(It.IsAny<string>())).Returns(true);
		SetupInstalledVersions(mocker, ("Addon", "1.0.0"), ("Library", "1.0.0"));
		SetupInstallations(mocker);
		var factory = mocker.GetMock<IMicroTasksFactory>();
		factory.Setup(x => x.CreateDownloadFileMicroTask(It.Is<DownloadFileSource>(source => ReferenceEquals(source.Value, addon.Items[1].ArchiveDownloadInfo))))
			.Returns(Marker("download Addon"));
		factory.Setup(x => x.CreateInstallModMicroTask("Addon", "addon-2.0.0.7z", null)).Returns(Marker("install Addon"));
		var installSteps = UseInstallStepsMicroTasksFactory(mocker);
		installSteps.Setup(x => x.CreateInstallMicroTasks(It.IsAny<IReadOnlyList<InstallStep>>(), It.IsAny<InstallBackupId?>(), It.IsAny<IDictionary<string, NuGetVersion>>())).Returns([]);
		Func<string, CancellationToken, ValueTask<bool>>? keepsMod = null;
		installSteps.Setup(x => x.CreateRollbackMicroTasks(addon.Items[0].InstallSteps!,
				InstallBackupId.ForModVersion("Addon", NuGetVersion.Parse("1.0.0")), It.IsAny<Func<string, CancellationToken, ValueTask<bool>>>()))
			.Callback((IReadOnlyList<InstallStep> _, InstallBackupId _, Func<string, CancellationToken, ValueTask<bool>> keeps) => keepsMod = keeps)
			.Returns([Marker("rollback Addon 1.0.0")]);
		modOrganizer.Setup(x => x.UninstallMod("Addon", CancellationToken.None)).Returns(Task.CompletedTask);
		mocker.GetMock<IInstallBackupService>().Setup(x => x.Delete(InstallBackupId.ForMod("Addon")));
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();
		var appTask = await sut.CreateModInstallWithDependenciesAppTask(addon, null, Reservations(_ => { }), CancellationToken.None);

		// Act
		await RunMicroTasks(mocker, appTask.Dependencies.Single());
		var names = await RunMicroTasks(mocker, appTask);
		var keepsLibrary = await keepsMod!("Library", CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(names.OfType<MicroTaskDescription.Custom>().Select(x => x.Text),
				Is.EqualTo(new[] { "rollback Addon 1.0.0", "download Addon", "install Addon" }));
			Assert.That(keepsLibrary, Is.True);
		});
	}

	[Test]
	public async Task CreateModInstallWithDependenciesAppTask_UpgradedDependencyInstalledATransitiveDependency_RollbackKeepsIt()
	{
		// Arrange
		var mocker = CreateDependencyMocker();
		var library = CreateLibrary();
		var core = new ModFeedData
		{
			Name = "Core", CategoryId = "mods", LogoPath = "core.png", DescriptionPath = new() { ["ru"] = "core.md" },
			Items =
			[
				new ModFeedItem
				{
					Version = NuGetVersion.Parse("1.0.0"),
					ArchiveDownloadInfo = new DownloadInfo { S3Path = "Mods/core-1.0.0.7z" },
					SupportedModpackVersion = VersionRange.Parse("[1.0.0,)"),
					InstallSteps = [InstallLibraryStep()],
				},
				new ModFeedItem
				{
					Version = NuGetVersion.Parse("2.0.0"),
					ArchiveDownloadInfo = new DownloadInfo { S3Path = "Mods/core-2.0.0.7z" },
					SupportedModpackVersion = VersionRange.Parse("[1.0.0,)"),
					Dependencies = [new ModDependency.AnotherMod([new ModDependencyAlternative("Library", VersionRange.Parse("[1.0.0, )"))])],
				},
			],
		};
		var addon = CreateAddon(new ModDependencyAlternative("Core", VersionRange.Parse("[2.0.0, )")));
		SetupFeed(mocker, library, core, addon);
		var modOrganizer = mocker.GetMock<IModOrganizerService>();
		modOrganizer.Setup(x => x.IsModInstalled(It.IsAny<string>())).Returns((string name) => name != "Addon");
		SetupInstalledVersions(mocker, ("Core", "1.0.0"), ("Library", "1.0.0"));
		SetupInstallations(mocker);
		var factory = mocker.GetMock<IMicroTasksFactory>();
		factory.Setup(x => x.CreateDownloadFileMicroTask(It.Is<DownloadFileSource>(source => ReferenceEquals(source.Value, core.Items[1].ArchiveDownloadInfo))))
			.Returns(Marker("download Core"));
		factory.Setup(x => x.CreateDownloadFileMicroTask(It.Is<DownloadFileSource>(source => ReferenceEquals(source.Value, addon.Items[0].ArchiveDownloadInfo))))
			.Returns(Marker("download Addon"));
		factory.Setup(x => x.CreateInstallModMicroTask("Core", "core-2.0.0.7z", null)).Returns(Marker("install Core"));
		factory.Setup(x => x.CreateInstallModMicroTask("Addon", "addon-1.0.0.7z", null)).Returns(Marker("install Addon"));
		var installSteps = UseInstallStepsMicroTasksFactory(mocker);
		installSteps.Setup(x => x.CreateInstallMicroTasks(It.IsAny<IReadOnlyList<InstallStep>>(), It.IsAny<InstallBackupId?>(), It.IsAny<IDictionary<string, NuGetVersion>>())).Returns([]);
		Func<string, CancellationToken, ValueTask<bool>>? keepsMod = null;
		installSteps.Setup(x => x.CreateRollbackMicroTasks(core.Items[0].InstallSteps!,
				InstallBackupId.ForModVersion("Core", NuGetVersion.Parse("1.0.0")), It.IsAny<Func<string, CancellationToken, ValueTask<bool>>>()))
			.Callback((IReadOnlyList<InstallStep> _, InstallBackupId _, Func<string, CancellationToken, ValueTask<bool>> keeps) => keepsMod = keeps)
			.Returns([Marker("rollback Core 1.0.0")]);
		modOrganizer.Setup(x => x.UninstallMod("Core", CancellationToken.None)).Returns(Task.CompletedTask);
		var backups = mocker.GetMock<IInstallBackupService>();
		backups.Setup(x => x.Delete(InstallBackupId.ForMod("Core")));
		backups.Setup(x => x.Delete(InstallBackupId.ForMod("Addon")));
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();
		var appTask = await sut.CreateModInstallWithDependenciesAppTask(addon, null, Reservations(_ => { }), CancellationToken.None);
		IReadOnlyList<ModUpgrade>? upgrades = null;

		// Act
		await RunMicroTasks(mocker, appTask.Dependencies.Single(), step =>
		{
			if (step is MicroTaskStep.ConfirmModUpgrades(var confirmation))
			{
				upgrades = confirmation.Upgrades;
				confirmation.SetResult(true);
			}
		});
		var names = await RunMicroTasks(mocker, appTask);
		var keepsLibrary = await keepsMod!("Library", CancellationToken.None);
		var keepsUndeclared = await keepsMod("Other", CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(upgrades, Is.EqualTo(new[] { new ModUpgrade(core, NuGetVersion.Parse("1.0.0"), NuGetVersion.Parse("2.0.0")) }));
			Assert.That(names.OfType<MicroTaskDescription.Custom>().Select(x => x.Text), Is.EqualTo(new[]
			{
				"rollback Core 1.0.0", "download Core", "install Core", "download Addon", "install Addon",
			}));
			Assert.That(keepsLibrary, Is.True);
			Assert.That(keepsUndeclared, Is.False);
		});
	}

	[Test]
	public async Task CreateModUninstallAppTask_ModWithDependencies_KeepsItsDependenciesInstalled()
	{
		// Arrange
		var mocker = CreateDependencyMocker();
		var addon = AddonWith(new ModFeedItem
		{
			Version = NuGetVersion.Parse("1.0.0"),
			ArchiveDownloadInfo = new DownloadInfo { S3Path = "Mods/addon-1.0.0.7z" },
			SupportedModpackVersion = VersionRange.Parse("[1.0.0,)"),
			Dependencies = [new ModDependency.AnotherMod([new ModDependencyAlternative("Library", VersionRange.Parse("[1.0.0, )"))])],
			SupportsUninstall = true,
		});
		var modOrganizer = mocker.GetMock<IModOrganizerService>();
		modOrganizer.Setup(x => x.IsModInstalled("Addon")).Returns(true);
		SetupInstalledVersions(mocker, ("Addon", "1.0.0"));
		modOrganizer.Setup(x => x.UninstallMod("Addon", CancellationToken.None)).Returns(Task.CompletedTask);
		var installSteps = UseInstallStepsMicroTasksFactory(mocker);
		installSteps.Setup(x => x.CreateRollbackMicroTasks(It.IsAny<IReadOnlyList<InstallStep>>(), It.IsAny<InstallBackupId>(),
			It.IsAny<Func<string, CancellationToken, ValueTask<bool>>>())).Returns([]);
		mocker.GetMock<IInstallBackupService>().Setup(x => x.Delete(InstallBackupId.ForMod("Addon")));
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();

		// Act
		await RunMicroTasks(mocker, sut.CreateModUninstallAppTask(addon));

		// Assert
		modOrganizer.Verify(x => x.UninstallMod(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
		modOrganizer.Verify(x => x.UninstallMod("Addon", CancellationToken.None), Times.Once);
	}

	[Test]
	public async Task CreateModUninstallAppTask_ModItInstalledIsDependencyOfAnotherInstalledMod_RollbackKeepsItWhileThatModIsInstalled()
	{
		// Arrange
		var mocker = CreateDependencyMocker();
		var library = CreateLibrary();
		var user = CreateLibrary("User");
		user = new ModFeedData
		{
			Name = user.Name, CategoryId = user.CategoryId, LogoPath = user.LogoPath, DescriptionPath = user.DescriptionPath,
			Items = [CreateAddon(new ModDependencyAlternative("Library", VersionRange.Parse("[1.0.0, )"))).Items[0]],
		};
		var addon = AddonWith(new ModFeedItem
		{
			Version = NuGetVersion.Parse("1.0.0"),
			ArchiveDownloadInfo = new DownloadInfo { S3Path = "Mods/addon-1.0.0.7z" },
			SupportedModpackVersion = VersionRange.Parse("[1.0.0,)"),
			InstallSteps = [InstallLibraryStep()],
			// The uninstalled mod's own declaration does not keep the mod.
			Dependencies = [new ModDependency.AnotherMod([new ModDependencyAlternative("Library", VersionRange.Parse("[1.0.0, )"))])],
			SupportsUninstall = true,
		});
		SetupFeed(mocker, library, user, addon);
		var isUserInstalled = true;
		var modOrganizer = mocker.GetMock<IModOrganizerService>();
		modOrganizer.Setup(x => x.IsModInstalled(It.IsAny<string>())).Returns((string name) => name != "User" || isUserInstalled);
		SetupInstalledVersions(mocker, ("Addon", "1.0.0"), ("Library", "1.0.0"), ("User", "1.0.0"));
		modOrganizer.Setup(x => x.UninstallMod("Addon", CancellationToken.None)).Returns(Task.CompletedTask);
		Func<string, CancellationToken, ValueTask<bool>>? keepsMod = null;
		UseInstallStepsMicroTasksFactory(mocker)
			.Setup(x => x.CreateRollbackMicroTasks(addon.Items[0].InstallSteps!,
				InstallBackupId.ForModVersion("Addon", NuGetVersion.Parse("1.0.0")), It.IsAny<Func<string, CancellationToken, ValueTask<bool>>>()))
			.Callback((IReadOnlyList<InstallStep> _, InstallBackupId _, Func<string, CancellationToken, ValueTask<bool>> keeps) => keepsMod = keeps)
			.Returns([]);
		mocker.GetMock<IInstallBackupService>().Setup(x => x.Delete(InstallBackupId.ForMod("Addon")));
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();
		await RunMicroTasks(mocker, sut.CreateModUninstallAppTask(addon));

		// Act
		var keptWhileUserInstalled = await keepsMod!("Library", CancellationToken.None);
		isUserInstalled = false;
		var keptAfterUserRemoved = await keepsMod("Library", CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(keptWhileUserInstalled, Is.True);
			Assert.That(keptAfterUserRemoved, Is.False);
		});
	}

	[Test]
	public async Task CreateModInstallWithDependenciesAppTask_RetryWhileCancelledResolutionStillRuns_ResolvesAndInstallsTheRetry()
	{
		// Arrange
		var mocker = CreateDependencyMocker();
		var library = CreateLibrary();
		var addon = CreateAddon(new ModDependencyAlternative("Library", VersionRange.Parse("[1.0.0, )")));
		SetupFeed(mocker, library, addon);
		var feedReads = 0;
		var firstFeedRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var heldFeed = new TaskCompletionSource<IReadOnlyList<ModFeedData>>(TaskCreationOptions.RunContinuationsAsynchronously);
		// The first resolution is held at reading the feed, whatever its cancellation.
		mocker.GetMock<IFeedProvider>().Setup(x => x.GetModFeedDatas(It.IsAny<CancellationToken>())).Returns(() =>
		{
			if (Interlocked.Increment(ref feedReads) > 1)
			{
				return ValueTask.FromResult<IReadOnlyList<ModFeedData>>([library, addon]);
			}

			firstFeedRead.SetResult();
			return new ValueTask<IReadOnlyList<ModFeedData>>(heldFeed.Task);
		});
		SetupNewModInstallations(mocker, library, addon);
		using var queue = CreateQueue(mocker);
		using var firstEnded = new CancellationTokenSource();
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();
		var first = await sut.CreateModInstallWithDependenciesAppTask(addon, null, Reservations(_ => { }, firstEnded.Token),
			CancellationToken.None);
		var firstInstalling = queue.EnqueueAndWaitForCompletion(first, CancellationToken.None);
		await TaskAssert.WithinTimeout(firstFeedRead.Task);
		await queue.CancelAppTask(first.Id, CancellationToken.None);
		await firstEnded.CancelAsync();
		await TaskAssert.WithinTimeout(firstInstalling);
		var reserved = new List<string>();
		var retry = await sut.CreateModInstallWithDependenciesAppTask(addon, null,
			Reservations(mods => reserved.AddRange(mods.Select(x => x.Name))), CancellationToken.None);

		// Act
		var retrying = queue.EnqueueAndWaitForCompletion(retry, CancellationToken.None);
		heldFeed.SetResult([library, addon]);
		await TaskAssert.WithinTimeout(retrying);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(first.IsUnneeded!(), Is.True);
			Assert.That(retry.IsUnneeded!(), Is.False);
			Assert.That(reserved, Is.EqualTo(new[] { "Library" }));
			Assert.That(queue.TaskCount, Is.Zero);
			mocker.GetMock<IModOrganizerService>().Verify(x => x.CreateOrUpdateMetaIniFile("Addon", It.IsAny<string?>(),
				It.IsAny<NuGetVersion>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
		});
	}

	[Test]
	public async Task CreateModInstallWithDependenciesAppTask_RequestEndsDuringLongSearch_SearchesOutsideTheQueueLockAndStops()
	{
		// Arrange
		var mocker = CreateDependencyMocker();
		var missing = new ModFeedData
		{
			Name = "Missing", CategoryId = "mods", LogoPath = "missing.png", DescriptionPath = new() { ["ru"] = "missing.md" },
			Items =
			[
				new ModFeedItem
				{
					Version = NuGetVersion.Parse("1.0.0"),
					ArchiveDownloadInfo = new DownloadInfo { S3Path = "Mods/missing-1.0.0.7z" },
					SupportedModpackVersion = VersionRange.Parse("[4.0.0,)"),
				},
			],
		};
		// Two-way entries, then one no version meets: without stopping, the search tries every combination.
		var pairs = Enumerable.Range(0, 16).Select(i => (CreateLibrary($"A{i}"), CreateLibrary($"B{i}"))).ToArray();
		var addon = AddonWith(new ModFeedItem
		{
			Version = NuGetVersion.Parse("1.0.0"),
			ArchiveDownloadInfo = new DownloadInfo { S3Path = "Mods/addon-1.0.0.7z" },
			SupportedModpackVersion = VersionRange.Parse("[1.0.0,)"),
			Dependencies =
			[
				..pairs.Select(pair => new ModDependency.AnotherMod(
				[
					new ModDependencyAlternative(pair.Item1.Name, VersionRange.Parse("[1.0.0, )")),
					new ModDependencyAlternative(pair.Item2.Name, VersionRange.Parse("[1.0.0, )")),
				])),
				new ModDependency.AnotherMod([new ModDependencyAlternative("Missing", VersionRange.Parse("[1.0.0, )"))]),
			],
		});
		using var requestEnded = new CancellationTokenSource();
		AppTaskQueue? queue = null;
		AppTask? appTask = null;
		var cancelledDuringSearch = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var mods = new EnumerationHookList([..pairs.SelectMany(pair => new[] { pair.Item1, pair.Item2 }), missing, addon], count =>
		{
			// Well into the search: removing the installation must not wait for it, and ending the request stops it.
			if (count == 100)
			{
				cancelledDuringSearch.SetResult(queue!.CancelAppTask(appTask!.Id, CancellationToken.None)
					.Wait(TimeSpan.FromSeconds(5)));
				requestEnded.Cancel();
			}
		});
		SetupFeed(mocker);
		mocker.GetMock<IFeedProvider>().Setup(x => x.GetModFeedDatas(It.IsAny<CancellationToken>())).ReturnsAsync(mods);
		mocker.GetMock<IModOrganizerService>().Setup(x => x.IsModInstalled(It.IsAny<string>())).Returns(false);
		queue = CreateQueue(mocker);
		using var disposeQueue = queue;
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();
		appTask = await sut.CreateModInstallWithDependenciesAppTask(addon, null, Reservations(_ => { }, requestEnded.Token),
			CancellationToken.None);

		// Act
		await TaskAssert.WithinTimeout(queue.EnqueueAndWaitForCompletion(appTask, CancellationToken.None));
		var cancelled = await TaskAssert.WithinTimeout(cancelledDuringSearch.Task);
		// A failed resolution would pause the queue, so this would not run.
		await TaskAssert.WithinTimeout(queue.EnqueueAndWaitForCompletion(
			new AppTask("Next", new AppTaskDescription.Custom("next"), _ => [], [], null), CancellationToken.None));

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(cancelled, Is.True);
			Assert.That(appTask.IsUnneeded!(), Is.True);
			Assert.That(queue.TaskCount, Is.Zero);
		});
	}

	[Test]
	public async Task CreateModInstallAppTask_BothVersionsInstallAModAnotherInstalledModDeclares_NewVersionReusesTheKeptMod()
	{
		// Arrange
		var mocker = CreateDependencyMocker();
		var addon = AddonWith(
			new ModFeedItem
			{
				Version = NuGetVersion.Parse("1.0.0"),
				ArchiveDownloadInfo = new DownloadInfo { S3Path = "Mods/addon-1.0.0.7z" },
				SupportedModpackVersion = VersionRange.Parse("[1.0.0,)"),
				InstallSteps = [InstallLibraryStep()],
			},
			new ModFeedItem
			{
				Version = NuGetVersion.Parse("2.0.0"),
				ArchiveDownloadInfo = new DownloadInfo { S3Path = "Mods/addon-2.0.0.7z" },
				SupportedModpackVersion = VersionRange.Parse("[1.0.0,)"),
				InstallSteps = [InstallLibraryStep()],
			});
		SetupFeed(mocker, CreateLibrary(), UserOf("Library"), addon);
		var modOrganizer = mocker.GetMock<IModOrganizerService>();
		modOrganizer.Setup(x => x.IsModInstalled(It.IsAny<string>())).Returns(true);
		SetupInstalledVersions(mocker, ("Addon", "1.0.0"), ("Library", "1.0.0"), ("User", "1.0.0"));
		SetupInstallations(mocker);
		var factory = mocker.GetMock<IMicroTasksFactory>();
		factory.Setup(x => x.CreateDownloadFileMicroTask(It.IsAny<DownloadFileSource>())).Returns(Marker("download Addon"));
		factory.Setup(x => x.CreateInstallModMicroTask("Addon", "addon-2.0.0.7z", null)).Returns(Marker("install Addon"));
		var installSteps = UseInstallStepsMicroTasksFactory(mocker);
		// The rollback of 1.0.0's step asks whether to keep Library, as the steps factory does.
		installSteps.Setup(x => x.CreateRollbackMicroTasks(addon.Items[0].InstallSteps!,
				InstallBackupId.ForModVersion("Addon", NuGetVersion.Parse("1.0.0")), It.IsAny<Func<string, CancellationToken, ValueTask<bool>>>()))
			.Returns((IReadOnlyList<InstallStep> _, InstallBackupId _, Func<string, CancellationToken, ValueTask<bool>> keepsMod) =>
				new IAppMicroTask[]
				{
					new DelegateMicroTask(new MicroTaskDescription.Custom("rollback Addon 1.0.0"), async (_, ct) => await keepsMod("Library", ct)),
				});
		bool? reusesLibrary = null;
		installSteps.Setup(x => x.CreateInstallMicroTasks(addon.Items[1].InstallSteps!,
				InstallBackupId.ForModVersion("Addon", NuGetVersion.Parse("2.0.0")), It.IsAny<IDictionary<string, NuGetVersion>>()))
			.Returns((IReadOnlyList<InstallStep> _, InstallBackupId? _, IDictionary<string, NuGetVersion> keptMods) =>
				new IAppMicroTask[]
				{
					new DelegateMicroTask(new MicroTaskDescription.Custom("steps Addon 2.0.0"), (_, _) =>
					{
						reusesLibrary = keptMods.ContainsKey("Library");
						return ValueTask.CompletedTask;
					}),
				});
		modOrganizer.Setup(x => x.UninstallMod("Addon", CancellationToken.None)).Returns(Task.CompletedTask);
		mocker.GetMock<IInstallBackupService>().Setup(x => x.Delete(InstallBackupId.ForMod("Addon")));
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();
		var appTask = await sut.CreateModInstallAppTask(addon, NuGetVersion.Parse("2.0.0"), CancellationToken.None);

		// Act
		var names = await RunMicroTasks(mocker, appTask);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(names.OfType<MicroTaskDescription.Custom>().Select(x => x.Text),
				Is.EqualTo(new[] { "rollback Addon 1.0.0", "download Addon", "install Addon", "steps Addon 2.0.0" }));
			Assert.That(reusesLibrary, Is.True);
		});
	}

	[Test]
	public async Task CreateModInstallAppTask_DownloadFailsAfterRollbackKeptAMod_ResumedAttemptReusesTheKeptMod()
	{
		// Arrange
		var mocker = CreateDependencyMocker();
		var addon = AddonWith(
			new ModFeedItem
			{
				Version = NuGetVersion.Parse("1.0.0"),
				ArchiveDownloadInfo = new DownloadInfo { S3Path = "Mods/addon-1.0.0.7z" },
				SupportedModpackVersion = VersionRange.Parse("[1.0.0,)"),
				InstallSteps = [InstallLibraryStep()],
			},
			new ModFeedItem
			{
				Version = NuGetVersion.Parse("2.0.0"),
				ArchiveDownloadInfo = new DownloadInfo { S3Path = "Mods/addon-2.0.0.7z" },
				SupportedModpackVersion = VersionRange.Parse("[1.0.0,)"),
				InstallSteps = [InstallLibraryStep()],
			});
		SetupFeed(mocker, CreateLibrary(), UserOf("Library"), addon);
		var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Addon", "Library", "User" };
		var modOrganizer = mocker.GetMock<IModOrganizerService>();
		modOrganizer.Setup(x => x.IsModInstalled(It.IsAny<string>())).Returns((string name) => installed.Contains(name));
		SetupInstalledVersions(mocker, ("Addon", "1.0.0"), ("Library", "1.0.0"), ("User", "1.0.0"));
		SetupInstallations(mocker);
		using var queue = CreateQueue(mocker);
		var downloads = 0;
		var factory = mocker.GetMock<IMicroTasksFactory>();
		factory.Setup(x => x.CreateDownloadFileMicroTask(It.IsAny<DownloadFileSource>())).Returns(
			new DelegateMicroTask(new MicroTaskDescription.Custom("download Addon"), (_, _) =>
				++downloads == 1 ? throw new IOException("Connection lost") : ValueTask.CompletedTask));
		factory.Setup(x => x.CreateInstallModMicroTask("Addon", "addon-2.0.0.7z", null)).Returns(Marker("install Addon"));
		var installSteps = UseInstallStepsMicroTasksFactory(mocker);
		installSteps.Setup(x => x.CreateRollbackMicroTasks(addon.Items[0].InstallSteps!,
				InstallBackupId.ForModVersion("Addon", NuGetVersion.Parse("1.0.0")), It.IsAny<Func<string, CancellationToken, ValueTask<bool>>>()))
			.Returns((IReadOnlyList<InstallStep> _, InstallBackupId _, Func<string, CancellationToken, ValueTask<bool>> keepsMod) =>
				new IAppMicroTask[]
				{
					new DelegateMicroTask(new MicroTaskDescription.Custom("rollback Addon 1.0.0"), async (_, ct) => await keepsMod("Library", ct)),
				});
		bool? reusesLibrary = null;
		installSteps.Setup(x => x.CreateInstallMicroTasks(addon.Items[1].InstallSteps!,
				InstallBackupId.ForModVersion("Addon", NuGetVersion.Parse("2.0.0")), It.IsAny<IDictionary<string, NuGetVersion>>()))
			.Returns((IReadOnlyList<InstallStep> _, InstallBackupId? _, IDictionary<string, NuGetVersion> keptMods) =>
				new IAppMicroTask[]
				{
					new DelegateMicroTask(new MicroTaskDescription.Custom("steps Addon 2.0.0"), (_, _) =>
					{
						reusesLibrary = keptMods.ContainsKey("Library");
						return ValueTask.CompletedTask;
					}),
				});
		modOrganizer.Setup(x => x.UninstallMod("Addon", It.IsAny<CancellationToken>())).Returns(() =>
		{
			installed.Remove("Addon");
			return Task.CompletedTask;
		});
		var backups = mocker.GetMock<IInstallBackupService>();
		backups.Setup(x => x.Delete(InstallBackupId.ForMod("Addon")));
		backups.Setup(x => x.StartInstallation(It.IsAny<InstallBackupId>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();
		var appTask = await sut.CreateModInstallAppTask(addon, NuGetVersion.Parse("2.0.0"), CancellationToken.None);
		var failures = 0;
		var failedAgain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		// Resumed once, like a user after the failed download; a second failure ends the test.
		using var subscription = queue.Events.Subscribe(e =>
		{
			if (e is AppTaskQueue.Event.TaskFailed(var failed, var exception) && failed == appTask)
			{
				if (++failures == 1)
				{
					queue.Resume();
				}
				else
				{
					failedAgain.TrySetException(exception);
				}
			}
		});

		// Act
		await TaskAssert.WithinTimeout(Task.WhenAny(
			queue.EnqueueAndWaitForCompletion(appTask, CancellationToken.None), failedAgain.Task).Unwrap());

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(downloads, Is.EqualTo(2));
			Assert.That(reusesLibrary, Is.True);
		});
	}

	[Test]
	public async Task CreateModInstallAppTask_KeptModReplacedByAnotherVersionBeforeResume_ResumedAttemptGetsTheKeptVersion()
	{
		// Arrange
		var mocker = CreateDependencyMocker();
		var addon = AddonWith(
			new ModFeedItem
			{
				Version = NuGetVersion.Parse("1.0.0"),
				ArchiveDownloadInfo = new DownloadInfo { S3Path = "Mods/addon-1.0.0.7z" },
				SupportedModpackVersion = VersionRange.Parse("[1.0.0,)"),
				InstallSteps = [InstallLibraryStep()],
			},
			new ModFeedItem
			{
				Version = NuGetVersion.Parse("2.0.0"),
				ArchiveDownloadInfo = new DownloadInfo { S3Path = "Mods/addon-2.0.0.7z" },
				SupportedModpackVersion = VersionRange.Parse("[1.0.0,)"),
				InstallSteps = [InstallLibraryStep()],
			});
		SetupFeed(mocker, CreateLibrary(), UserOf("Library"), addon);
		var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Addon", "Library", "User" };
		var versions = new Dictionary<string, string> { ["Addon"] = "1.0.0", ["Library"] = "1.0.0", ["User"] = "1.0.0" };
		var modOrganizer = mocker.GetMock<IModOrganizerService>();
		modOrganizer.Setup(x => x.IsModInstalled(It.IsAny<string>())).Returns((string name) => installed.Contains(name));
		modOrganizer.SetupGet(x => x.InstalledMods).Returns(() =>
			[..versions.Select(x => new ModInfo(x.Key, new Lazy<MetaIniData?>(() => new MetaIniData { Comments = null, Version = x.Value })))]);
		SetupInstallations(mocker);
		using var queue = CreateQueue(mocker);
		var downloads = 0;
		var factory = mocker.GetMock<IMicroTasksFactory>();
		factory.Setup(x => x.CreateDownloadFileMicroTask(It.IsAny<DownloadFileSource>())).Returns(
			new DelegateMicroTask(new MicroTaskDescription.Custom("download Addon"), (_, _) =>
				++downloads == 1 ? throw new IOException("Connection lost") : ValueTask.CompletedTask));
		factory.Setup(x => x.CreateInstallModMicroTask("Addon", "addon-2.0.0.7z", null)).Returns(Marker("install Addon"));
		var installSteps = UseInstallStepsMicroTasksFactory(mocker);
		installSteps.Setup(x => x.CreateRollbackMicroTasks(addon.Items[0].InstallSteps!,
				InstallBackupId.ForModVersion("Addon", NuGetVersion.Parse("1.0.0")), It.IsAny<Func<string, CancellationToken, ValueTask<bool>>>()))
			.Returns((IReadOnlyList<InstallStep> _, InstallBackupId _, Func<string, CancellationToken, ValueTask<bool>> keepsMod) =>
				new IAppMicroTask[]
				{
					new DelegateMicroTask(new MicroTaskDescription.Custom("rollback Addon 1.0.0"), async (_, ct) => await keepsMod("Library", ct)),
				});
		var keptModsOfRuns = new List<Dictionary<string, NuGetVersion>>();
		installSteps.Setup(x => x.CreateInstallMicroTasks(addon.Items[1].InstallSteps!,
				InstallBackupId.ForModVersion("Addon", NuGetVersion.Parse("2.0.0")), It.IsAny<IDictionary<string, NuGetVersion>>()))
			.Returns((IReadOnlyList<InstallStep> _, InstallBackupId? _, IDictionary<string, NuGetVersion> keptMods) =>
				new IAppMicroTask[]
				{
					new DelegateMicroTask(new MicroTaskDescription.Custom("steps Addon 2.0.0"), (_, _) =>
					{
						keptModsOfRuns.Add(new Dictionary<string, NuGetVersion>(keptMods));
						return ValueTask.CompletedTask;
					}),
				});
		modOrganizer.Setup(x => x.UninstallMod("Addon", It.IsAny<CancellationToken>())).Returns(() =>
		{
			installed.Remove("Addon");
			versions.Remove("Addon");
			return Task.CompletedTask;
		});
		var backups = mocker.GetMock<IInstallBackupService>();
		backups.Setup(x => x.Delete(InstallBackupId.ForMod("Addon")));
		backups.Setup(x => x.StartInstallation(It.IsAny<InstallBackupId>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();
		var appTask = await sut.CreateModInstallAppTask(addon, NuGetVersion.Parse("2.0.0"), CancellationToken.None);
		var failures = 0;
		var failedAgain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		// After the failed download the user replaces the kept Library 1.0.0 with 2.0.0, then resumes once.
		using var subscription = queue.Events.Subscribe(e =>
		{
			if (e is AppTaskQueue.Event.TaskFailed(var failed, var exception) && failed == appTask)
			{
				if (++failures == 1)
				{
					versions["Library"] = "2.0.0";
					queue.Resume();
				}
				else
				{
					failedAgain.TrySetException(exception);
				}
			}
		});

		// Act
		await TaskAssert.WithinTimeout(Task.WhenAny(
			queue.EnqueueAndWaitForCompletion(appTask, CancellationToken.None), failedAgain.Task).Unwrap());

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(downloads, Is.EqualTo(2));
			Assert.That(keptModsOfRuns, Has.Count.EqualTo(1));
			Assert.That(keptModsOfRuns.Single(), Is.EquivalentTo(new Dictionary<string, NuGetVersion>
			{
				["Library"] = NuGetVersion.Parse("1.0.0"),
			}));
		});
	}

	[Test]
	public async Task CreateModUninstallAppTask_StepNamesDependencyInAnotherCase_RollbackKeepsIt()
	{
		// Arrange
		var mocker = CreateDependencyMocker();
		var addon = AddonWith(new ModFeedItem
		{
			Version = NuGetVersion.Parse("1.0.0"),
			ArchiveDownloadInfo = new DownloadInfo { S3Path = "Mods/addon-1.0.0.7z" },
			SupportedModpackVersion = VersionRange.Parse("[1.0.0,)"),
			InstallSteps =
			[
				new InstallStep
				{
					Actions = [new InstallAction.InstallMod(new DownloadInfo { S3Path = "Mods/library-1.0.0.7z" }, "library")],
					CanRollback = true,
				},
			],
			SupportsUninstall = true,
		});
		SetupFeed(mocker, CreateLibrary(), UserOf("Library"), addon);
		var modOrganizer = mocker.GetMock<IModOrganizerService>();
		modOrganizer.Setup(x => x.IsModInstalled(It.IsAny<string>())).Returns(true);
		SetupInstalledVersions(mocker, ("Addon", "1.0.0"), ("Library", "1.0.0"), ("User", "1.0.0"));
		modOrganizer.Setup(x => x.UninstallMod("Addon", CancellationToken.None)).Returns(Task.CompletedTask);
		Func<string, CancellationToken, ValueTask<bool>>? keepsMod = null;
		UseInstallStepsMicroTasksFactory(mocker)
			.Setup(x => x.CreateRollbackMicroTasks(addon.Items[0].InstallSteps!,
				InstallBackupId.ForModVersion("Addon", NuGetVersion.Parse("1.0.0")), It.IsAny<Func<string, CancellationToken, ValueTask<bool>>>()))
			.Callback((IReadOnlyList<InstallStep> _, InstallBackupId _, Func<string, CancellationToken, ValueTask<bool>> keeps) => keepsMod = keeps)
			.Returns([]);
		mocker.GetMock<IInstallBackupService>().Setup(x => x.Delete(InstallBackupId.ForMod("Addon")));
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();
		await RunMicroTasks(mocker, sut.CreateModUninstallAppTask(addon));

		// Act
		var keepsLibrary = await keepsMod!("library", CancellationToken.None);

		// Assert
		Assert.That(keepsLibrary, Is.True);
	}

	[TestCase("1.0.0", "Part", false)]
	[TestCase("2.0.0", "Part", true)]
	// The step spells the feed mod Part in another case: its meta.ini still tells the installed version.
	[TestCase("2.0.0", "part", true)]
	public async Task CreateModInstallWithDependenciesAppTask_EarlierDependencyInstallsALaterOne_RechecksItWhenItsTurnComes(
		string versionInstalledByBundle, string modNameInBundleStep, bool fails)
	{
		// Arrange
		var mocker = CreateDependencyMocker();
		var part = new ModFeedData
		{
			Name = "Part", CategoryId = "mods", LogoPath = "part.png", DescriptionPath = new() { ["ru"] = "part.md" },
			Items =
			[
				CreateLibrary("Part").Items[0],
				new ModFeedItem
				{
					Version = NuGetVersion.Parse("2.0.0"),
					ArchiveDownloadInfo = new DownloadInfo { S3Path = "Mods/part-2.0.0.7z" },
					SupportedModpackVersion = VersionRange.Parse("[1.0.0,)"),
				},
			],
		};
		// A legacy step of Bundle installs Part itself.
		var bundle = new ModFeedData
		{
			Name = "Bundle", CategoryId = "mods", LogoPath = "bundle.png", DescriptionPath = new() { ["ru"] = "bundle.md" },
			Items =
			[
				new ModFeedItem
				{
					Version = NuGetVersion.Parse("1.0.0"),
					ArchiveDownloadInfo = new DownloadInfo { S3Path = "Mods/bundle-1.0.0.7z" },
					SupportedModpackVersion = VersionRange.Parse("[1.0.0,)"),
					InstallSteps =
					[
						new InstallStep
						{
							Actions = [new InstallAction.InstallMod(new DownloadInfo { S3Path = "Mods/part.7z" }, modNameInBundleStep)],
							CanRollback = true,
						},
					],
				},
			],
		};
		var addon = AddonWith(new ModFeedItem
		{
			Version = NuGetVersion.Parse("1.0.0"),
			ArchiveDownloadInfo = new DownloadInfo { S3Path = "Mods/addon-1.0.0.7z" },
			SupportedModpackVersion = VersionRange.Parse("[1.0.0,)"),
			Dependencies =
			[
				new ModDependency.AnotherMod([new ModDependencyAlternative("Bundle", VersionRange.Parse("[1.0.0, )"))]),
				new ModDependency.AnotherMod([new ModDependencyAlternative("Part", VersionRange.Parse("[1.0.0, 2.0.0)"))]),
			],
		});
		SetupFeed(mocker, part, bundle, addon);
		// Mod directories are matched like Windows paths.
		var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		mocker.GetMock<IModOrganizerService>().Setup(x => x.IsModInstalled(It.IsAny<string>()))
			.Returns((string name) => installed.Contains(name));
		SetupInstalledVersions(mocker, ("Bundle", "1.0.0"), (modNameInBundleStep, versionInstalledByBundle));
		SetupInstallations(mocker);
		var factory = mocker.GetMock<IMicroTasksFactory>();
		factory.Setup(x => x.CreateDownloadFileMicroTask(It.IsAny<DownloadFileSource>())).Returns(Marker("download"));
		factory.Setup(x => x.CreateInstallModMicroTask(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ModPlacement?>()))
			.Returns((string name, string _, ModPlacement? _) => Installing(name));
		var installSteps = UseInstallStepsMicroTasksFactory(mocker);
		installSteps.Setup(x => x.CreateInstallMicroTasks(It.IsAny<IReadOnlyList<InstallStep>>(), It.IsAny<InstallBackupId?>(),
			It.IsAny<IDictionary<string, NuGetVersion>>())).Returns([]);
		installSteps.Setup(x => x.CreateInstallMicroTasks(bundle.Items[0].InstallSteps!, It.IsAny<InstallBackupId?>(),
			It.IsAny<IDictionary<string, NuGetVersion>>())).Returns([Installing(modNameInBundleStep)]);
		mocker.GetMock<IInstallBackupService>().Setup(x => x.Delete(It.IsAny<InstallBackupId>()));
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();
		var appTask = await sut.CreateModInstallWithDependenciesAppTask(addon, null, Reservations(_ => { }), CancellationToken.None);
		await RunMicroTasks(mocker, appTask.Dependencies.Single());

		// Act
		async Task<object[]> Act() => await RunMicroTasks(mocker, appTask);

		// Assert
		if (fails)
		{
			var exception = await Assert.ThrowsAsync<ModDependencyUnavailableException>(Act);
			Assert.Multiple(() =>
			{
				Assert.That(exception!.Alternatives.Single().UnavailableReason,
					Is.EqualTo((ModDependencyUnavailableReason)new ModDependencyUnavailableReason.NewerVersionInstalled(NuGetVersion.Parse("2.0.0"))));
				Assert.That(installed, Is.EquivalentTo(new[] { "Bundle", modNameInBundleStep }));
			});
			return;
		}

		var names = await Act();
		Assert.That(names.OfType<MicroTaskDescription.Custom>().Select(x => x.Text), Is.EqualTo(new[]
		{
			"download", "install Bundle", "install Part", "download", "install Addon",
		}));

		DelegateMicroTask Installing(string name) =>
			new(new MicroTaskDescription.Custom($"install {name}"), (_, _) =>
			{
				installed.Add(name);
				return ValueTask.CompletedTask;
			});
	}

	[Test]
	public async Task CreateModInstallWithDependenciesAppTask_SameModRequestedWhenItsInstallationCompletes_InstallsTheNewRequest()
	{
		// Arrange
		var mocker = CreateDependencyMocker();
		var library = CreateLibrary();
		var addon = CreateAddon(new ModDependencyAlternative("Library", VersionRange.Parse("[1.0.0, )")));
		SetupFeed(mocker, library, addon);
		SetupNewModInstallations(mocker, library, addon);
		using var queue = CreateQueue(mocker);
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();
		var first = await sut.CreateModInstallWithDependenciesAppTask(addon, null, Reservations(_ => { }), CancellationToken.None);
		var secondRequested = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
		// Like a synchronous subscriber of the installation's end, which runs before the queue clears the finished task.
		using var subscription = queue.Events.Subscribe(e =>
		{
			if (e is AppTaskQueue.Event.TaskCompleted(var completed) && completed == first)
			{
				var second = sut.CreateModInstallWithDependenciesAppTask(addon, null, Reservations(_ => { }), CancellationToken.None);
				secondRequested.SetResult(second.IsCompletedSuccessfully
					? queue.EnqueueAndWaitForCompletion(second.Result, CancellationToken.None)
					: Task.FromException(new InvalidOperationException("The task was not created synchronously")));
			}
		});

		// Act
		await TaskAssert.WithinTimeout(queue.EnqueueAndWaitForCompletion(first, CancellationToken.None));
		var secondInstalling = await TaskAssert.WithinTimeout(secondRequested.Task);
		await TaskAssert.WithinTimeout(secondInstalling);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(queue.TaskCount, Is.Zero);
			mocker.GetMock<IModOrganizerService>().Verify(x => x.CreateOrUpdateMetaIniFile("Addon", It.IsAny<string?>(),
				It.IsAny<NuGetVersion>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
		});
	}

	[Test]
	public async Task CreateModInstallWithDependenciesAppTask_DependentsShareDisplayName_AsksWithDistinctStepTitles()
	{
		// Arrange
		var mocker = CreateDependencyMocker();
		ModFeedData[] libraries = [CreateLibrary("A1"), CreateLibrary("A2"), CreateLibrary("B1"), CreateLibrary("B2")];
		var coreA = CoreRequiring("Core A", "A1", "A2");
		var coreB = CoreRequiring("Core B", "B1", "B2");
		var addon = AddonWith(new ModFeedItem
		{
			Version = NuGetVersion.Parse("1.0.0"),
			ArchiveDownloadInfo = new DownloadInfo { S3Path = "Mods/addon-1.0.0.7z" },
			SupportedModpackVersion = VersionRange.Parse("[1.0.0,)"),
			Dependencies =
			[
				new ModDependency.AnotherMod([new ModDependencyAlternative("Core A", VersionRange.Parse("[1.0.0, )"))]),
				new ModDependency.AnotherMod([new ModDependencyAlternative("Core B", VersionRange.Parse("[1.0.0, )"))]),
			],
		});
		SetupFeed(mocker, [..libraries, coreA, coreB, addon]);
		mocker.GetMock<IModOrganizerService>().Setup(x => x.IsModInstalled(It.IsAny<string>())).Returns(false);
		var sut = mocker.CreateInstance<InstallAppTaskProvider>();
		var appTask = await sut.CreateModInstallWithDependenciesAppTask(addon, null, Reservations(_ => { }), CancellationToken.None);
		var titles = new List<ChoiceText>();

		// Act
		await RunMicroTasks(mocker, appTask.Dependencies.Single(), step =>
		{
			if (step is MicroTaskStep.SelectChoices(var context))
			{
				var choiceStep = context.Steps.Single();
				titles.Add(choiceStep.Title);
				context.SetResult([new ChoiceStepSelection { Step = choiceStep, SelectedOptions = [choiceStep.Options[0]] }]);
			}
		});

		// Assert
		Assert.That(titles, Is.EqualTo(new ChoiceText[]
		{
			new ChoiceText.ModDependencyStepTitle("Core A", 1), new ChoiceText.ModDependencyStepTitle("Core B", 1),
		}));

		static ModFeedData CoreRequiring(string name, string first, string second) => new()
		{
			Name = name, DisplayName = new() { ["ru"] = "Core" }, CategoryId = "mods", LogoPath = "core.png",
			DescriptionPath = new() { ["ru"] = "core.md" },
			Items =
			[
				CreateAddon(new ModDependencyAlternative(first, VersionRange.Parse("[1.0.0, )")),
					new ModDependencyAlternative(second, VersionRange.Parse("[1.0.0, )"))).Items[0],
			],
		};
	}

	/// <summary>The mod <c>User</c> 1.0.0, installed with the others, requiring <paramref name="dependency"/>.</summary>
	private static ModFeedData UserOf(string dependency) => new()
	{
		Name = "User", CategoryId = "mods", LogoPath = "user.png", DescriptionPath = new() { ["ru"] = "user.md" },
		Items = [CreateAddon(new ModDependencyAlternative(dependency, VersionRange.Parse("[1.0.0, )"))).Items[0]],
	};

	/// <summary>
	/// A mocker whose installed modpack is 3.0.0, whose prerequisite is only the 7-Zip task and which logs nothing.
	/// </summary>
	private static AutoMocker CreateDependencyMocker()
	{
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		mocker.Use<ILogger<InstallAppTaskProvider>>(NullLogger<InstallAppTaskProvider>.Instance);
		var prerequisites = mocker.GetMock<IPrerequisitesAppTaskProvider>();
		prerequisites.SetupGet(x => x.InstallVcRedistTasks).Returns([]);
		prerequisites.Setup(x => x.CreateInstallSevenZipTask(CancellationToken.None))
			.ReturnsAsync(new AppTask("InstallSevenZip", new AppTaskDescription.Custom("7-Zip"), _ => [], [], null));
		mocker.GetMock<IModpackVersionProvider>().SetupGet(x => x.InstalledModpackVersion).Returns(NuGetVersion.Parse("3.0.0"));
		return mocker;
	}

	private static void SetupFeed(AutoMocker mocker, params ModFeedData[] mods)
	{
		var feed = mocker.GetMock<IFeedProvider>();
		feed.Setup(x => x.GetModFeedDatas(It.IsAny<CancellationToken>())).ReturnsAsync(mods);
		feed.Setup(x => x.GetModCategories(It.IsAny<CancellationToken>()))
			.ReturnsAsync([new ModCategoryFeedData { Id = "mods", Title = new() { ["ru"] = "Моды" } }]);
		SetupUnfinishedInstallation(mocker, null);
	}

	private static ModDependencyReservations Reservations(Action<IReadOnlyList<ModFeedData>> reserve,
		CancellationToken requestEnded = default) =>
		new(() => [], (mods, _) =>
		{
			reserve(mods);
			return null;
		}, requestEnded);

	private static ModFeedData CreateLibrary(string name = "Library", bool requiresNewGame = false) => new()
	{
		Name = name,
		CategoryId = "mods",
		LogoPath = "library.png",
		DescriptionPath = new() { ["ru"] = "library.md" },
		RequiresNewGame = requiresNewGame,
		Items =
		[
			new ModFeedItem
			{
				Version = NuGetVersion.Parse("1.0.0"),
				ArchiveDownloadInfo = new DownloadInfo { S3Path = "Mods/library-1.0.0.7z" },
				SupportedModpackVersion = VersionRange.Parse("[1.0.0,)"),
				SupportsUninstall = true,
			},
		],
	};

	private static ModFeedData AddonWith(params ModFeedItem[] items) => new()
	{
		Name = "Addon",
		CategoryId = "mods",
		LogoPath = "addon.png",
		DescriptionPath = new() { ["ru"] = "addon.md" },
		Items = items,
	};

	/// <summary>The mod <c>Addon</c> 1.0.0, supporting modpack 1.0.0 and later, requiring one of <paramref name="alternatives"/>.</summary>
	private static ModFeedData CreateAddon(params ModDependencyAlternative[] alternatives) => new()
	{
		Name = "Addon",
		CategoryId = "mods",
		LogoPath = "addon.png",
		DescriptionPath = new() { ["ru"] = "addon.md" },
		Items =
		[
			new ModFeedItem
			{
				Version = NuGetVersion.Parse("1.0.0"),
				ArchiveDownloadInfo = new DownloadInfo { S3Path = "Mods/addon-1.0.0.7z" },
				SupportedModpackVersion = VersionRange.Parse("[1.0.0,)"),
				Dependencies = alternatives.Length > 0 ? [new ModDependency.AnotherMod(alternatives)] : null,
			},
		],
	};

	/// <summary>
	/// Builds the task's microtasks lazily, like the queue, running each before creating the next, and returns their
	/// descriptions; <paramref name="onStep"/> sees every step they report.
	/// </summary>
	private static async Task<object[]> RunMicroTasks(AutoMocker mocker, AppTask appTask, Action<MicroTaskStep>? onStep = null)
	{
		SetupDelegateMicroTasks(mocker);
		using var subject = new Subject<MicroTaskStep>();
		using var subscription = subject.Subscribe(step => onStep?.Invoke(step));

		var descriptions = new List<object>();
		foreach (var microTask in appTask.MicroTasksProvider(CreateMicroTasksProviderContext(mocker)))
		{
			descriptions.Add(microTask.Description.Value);
			await microTask.Execute(subject, CancellationToken.None);
		}

		return [..descriptions];
	}

	private static MicroTasksProviderContext CreateMicroTasksProviderContext(AutoMocker mocker) =>
		new(mocker.GetMock<IMicroTasksFactory>().Object, mocker.GetMock<IServiceProvider>().Object);

	private static void SetupDelegateMicroTasks(AutoMocker mocker) =>
		mocker.GetMock<IMicroTasksFactory>()
			.Setup(x => x.CreateDelegateMicroTask(
				It.IsAny<Func<ISubject<MicroTaskStep>, CancellationToken, ValueTask>>(), It.IsAny<MicroTaskDescription>()))
			.Returns<Func<ISubject<MicroTaskStep>, CancellationToken, ValueTask>, MicroTaskDescription>(
				(func, description) => new DelegateMicroTask(description, func));

	/// <summary>A real queue running each task in a scope whose services are the task service provider mock's.</summary>
	private static AppTaskQueue CreateQueue(AutoMocker mocker)
	{
		SetupDelegateMicroTasks(mocker);
		var serviceProvider = mocker.GetMock<IServiceProvider>();
		serviceProvider.Setup(x => x.GetService(typeof(IMicroTasksFactory))).Returns(mocker.GetMock<IMicroTasksFactory>().Object);
		var queueMocker = new AutoMocker(MockBehavior.Strict);
		queueMocker.Use<ILogger<AppTaskQueue>>(NullLogger<AppTaskQueue>.Instance);
		queueMocker.Use(TimeProvider.System);
		var scope = queueMocker.GetMock<IServiceScope>();
		scope.SetupGet(x => x.ServiceProvider).Returns(serviceProvider.Object);
		scope.Setup(x => x.Dispose());
		queueMocker.GetMock<IServiceScopeFactory>().Setup(x => x.CreateScope()).Returns(scope.Object);
		return queueMocker.CreateInstance<AppTaskQueue>();
	}

	/// <summary>A rollbackable step installing the mod <c>Library</c> from its 1.0.0 archive.</summary>
	private static InstallStep InstallLibraryStep() => new()
	{
		Actions = [new InstallAction.InstallMod(new DownloadInfo { S3Path = "Mods/library-1.0.0.7z" }, "Library")],
		CanRollback = true,
	};

	/// <summary>Makes the meta.ini of each of <paramref name="mods"/> record its version.</summary>
	private static void SetupInstalledVersions(AutoMocker mocker, params (string Name, string Version)[] mods) =>
		mocker.GetMock<IModOrganizerService>().SetupGet(x => x.InstalledMods).Returns(
			[..mods.Select(x => new ModInfo(x.Name, new Lazy<MetaIniData?>(() => new MetaIniData { Comments = null, Version = x.Version })))]);

	/// <summary>Lets installations write meta.ini with the stored path of any downloaded archive.</summary>
	private static void SetupInstallations(AutoMocker mocker)
	{
		mocker.GetMock<IModOrganizerService>().Setup(x => x.CreateOrUpdateMetaIniFile(It.IsAny<string>(), It.IsAny<string?>(),
			It.IsAny<NuGetVersion>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		mocker.GetMock<IFileStorageService>().Setup(x => x.GetFilePath(It.IsAny<string>())).Returns(@"C:\Downloads\file.7z");
	}

	/// <summary>Lets <paramref name="mods"/>, none of them installed, be installed in a queue's scope, without install steps.</summary>
	private static void SetupNewModInstallations(AutoMocker mocker, params ModFeedData[] mods)
	{
		mocker.GetMock<IModOrganizerService>().Setup(x => x.IsModInstalled(It.IsAny<string>())).Returns(false);
		SetupInstallations(mocker);
		var factory = mocker.GetMock<IMicroTasksFactory>();
		factory.Setup(x => x.CreateDownloadFileMicroTask(It.IsAny<DownloadFileSource>())).Returns(Marker("download"));
		factory.Setup(x => x.CreateInstallModMicroTask(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ModPlacement?>()))
			.Returns(Marker("install"));
		UseInstallStepsMicroTasksFactory(mocker)
			.Setup(x => x.CreateInstallMicroTasks(It.IsAny<IReadOnlyList<InstallStep>>(), It.IsAny<InstallBackupId?>(), It.IsAny<IDictionary<string, NuGetVersion>>())).Returns([]);
		var backups = mocker.GetMock<IInstallBackupService>();
		backups.Setup(x => x.StartInstallation(It.IsAny<InstallBackupId>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		foreach (var mod in mods)
		{
			backups.Setup(x => x.Delete(InstallBackupId.ForMod(mod.Name)));
		}
	}

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
		CategoryId = "mods",
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
			installSteps.Setup(x => x.CreateInstallMicroTasks(patch.InstallSteps!, BackupId(version), It.IsAny<IDictionary<string, NuGetVersion>>())).Returns([Marker($"steps {version}")]);
			mocker.GetMock<IModOrganizerService>()
				.Setup(x => x.CreateOrUpdateMetaIniFile("Mod", @"C:\Downloads\mod-2.0.0.7z", patch.Version, "Mod",
					CancellationToken.None))
				.Returns(Task.CompletedTask);
		}
	}
}
