using System.Diagnostics;
using NuGet.Versioning;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.AppTasks.InstallSteps;
using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.Core.AppTasks.ModpackComponents;
using LauncherKit.Core.Backups;
using LauncherKit.Core.Choices;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.GraphPacks;
using LauncherKit.Core.Feed.Install;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.Feed.Modpacks;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Infrastructure;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.AppTasks;

[TestFixture]
public sealed class AppTaskProviderTests
{
	[Test]
	public void CreateUpdateLauncherTask_DownloadInfo_ReturnsStandaloneUpdateTask()
	{
		// Arrange
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		OptionsStubs.Snapshot(mocker, new AppSettings { SkyrimDirectory = @"C:\Skyrim" });
		var sut = mocker.CreateInstance<AppTaskProvider>();
		var download = new DownloadInfo { S3Path = "launcher.zip" };

		// Act
		var task = sut.CreateUpdateLauncherTask(download, NuGetVersion.Parse("1.2.3"));

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(task.Id, Is.EqualTo("UpdateLauncher"));
			Assert.That(task.Description.Value, Is.EqualTo(new AppTaskDescription.UpdateLauncher(NuGetVersion.Parse("1.2.3"))));
			Assert.That(task.Dependencies, Is.Empty);
			Assert.That(task.CompleteVerifier, Is.Null);
		});
	}

	[TestCase("RunGame")]
	[TestCase("UpdateLauncher")]
	[TestCase("InstallSkyrim")]
	[TestCase("InstallSevenZip")]
	[TestCase("Install_Lite")]
	[TestCase("Uninstall_Lite")]
	public void IsModpackInstallationTask_TaskOutsideModpackChain_ReturnsFalse(string taskId)
	{
		// Act
		var result = AppTaskProvider.IsModpackInstallationTask(taskId);

		// Assert
		Assert.That(result, Is.False);
	}

	[TestCase("SelectModpackComponents")]
	[TestCase("InstallModpack")]
	[TestCase("UninstallModpack")]
	[TestCase("InstallModpackPatch_3.3.1")]
	[TestCase("InstallModpackComponent_EnglishVoices")]
	[TestCase("InstallModpackComponent_ArbitraryFeedId")]
	public void IsModpackInstallationTask_ComponentOrModpackTask_ReturnsTrue(string taskId)
	{
		Assert.That(AppTaskProvider.IsModpackInstallationTask(taskId), Is.True);
	}

	[Test]
	public void CreateUninstallModpackTask_Always_ReturnsStandaloneTaskThatLocksModpackInstallation()
	{
		// Arrange
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		OptionsStubs.Snapshot(mocker, new AppSettings { SkyrimDirectory = @"C:\Skyrim" });
		var sut = mocker.CreateInstance<AppTaskProvider>();

		// Act
		var task = sut.CreateUninstallModpackTask();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(task.Dependencies, Is.Empty);
			Assert.That(task.CompleteVerifier, Is.Null);
			Assert.That(AppTaskProvider.IsModpackInstallationTask(task.Id), Is.True);
		});
	}

	[Test]
	public async Task CreateUninstallModpackTask_Run_DeletesModOrganizerThenAllBackups()
	{
		// Arrange
		var calls = new List<string>();
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		OptionsStubs.Snapshot(mocker, new AppSettings { SkyrimDirectory = @"C:\Skyrim" });
		var factory = mocker.GetMock<IMicroTasksFactory>();
		MicroTaskDescription deleteModOrganizer = MicroTaskDescription.DeleteModOrganizer.Instance;
		factory.Setup(x => x.CreateDeleteFileSystemItemsMicroTask(
				It.Is<IReadOnlyCollection<SkyrimRelativePath>>(paths => paths.Single().Path == "Example MO"), true,
				deleteModOrganizer))
			.Returns(new DelegateMicroTask(new MicroTaskDescription.Custom("delete"), (_, _) =>
			{
				calls.Add("delete Mod Organizer");
				return ValueTask.CompletedTask;
			}));
		factory.Setup(x => x.CreateDelegateMicroTask(
				It.IsAny<Func<R3.ISubject<MicroTaskStep>, CancellationToken, ValueTask>>(), It.IsAny<MicroTaskDescription>()))
			.Returns<Func<R3.ISubject<MicroTaskStep>, CancellationToken, ValueTask>, MicroTaskDescription>(
				(func, description) => new DelegateMicroTask(description, func));
		var backups = mocker.GetMock<IInstallBackupService>();
		backups.Setup(x => x.DeleteAll()).Callback(() => calls.Add("delete backups"));
		mocker.GetMock<IServiceProvider>()
			.Setup(x => x.GetService(typeof(IInstallBackupService)))
			.Returns(backups.Object);
		var sut = mocker.CreateInstance<AppTaskProvider>();
		var task = sut.CreateUninstallModpackTask();
		using var subject = new R3.Subject<MicroTaskStep>();

		// Act
		foreach (var microTask in task.MicroTasksProvider(CreateMicroTasksProviderContext(mocker)))
		{
			await microTask.Execute(subject, CancellationToken.None);
		}

		// Assert
		Assert.That(calls, Is.EqualTo(new[] { "delete Mod Organizer", "delete backups" }));
	}

	[Test]
	public async Task CreateInstallPatchTask_WithInstallSteps_RunsStepsBeforePublishingModpackVersion()
	{
		// Arrange
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		OptionsStubs.Snapshot(mocker, new AppSettings { SkyrimDirectory = @"C:\Skyrim" });
		var prerequisites = mocker.GetMock<IPrerequisitesAppTaskProvider>();
		prerequisites.SetupGet(x => x.InstallVcRedistTasks).Returns([]);
		prerequisites.Setup(x => x.CreateInstallSevenZipTask(CancellationToken.None))
			.ReturnsAsync(new AppTask("InstallSevenZip", new AppTaskDescription.Custom("7-Zip"), _ => [], [], null));
		mocker.GetMock<IFeedProvider>().Setup(x => x.GetSkyrimDownloadInfo(CancellationToken.None))
			.ReturnsAsync(new DownloadInfo { S3Path = "skyrim.7z" });
		var patch = new ModpackPatchFeedItem
		{
			Version = NuGetVersion.Parse("3.2.1"),
			DownloadInfo = new DownloadInfo { S3Path = "patch.7z" },
			InstallSteps = [new InstallStep { Actions = [new InstallAction.RemoveFileSystemItem(["old.esp"])] }],
		};
		var factory = mocker.GetMock<IMicroTasksFactory>();
		factory.Setup(x => x.CreateDownloadFileMicroTask(It.Is<DownloadFileSource>(source => ReferenceEquals(source.Value, patch.DownloadInfo))))
			.Returns(Marker("download"));
		MicroTaskDescription extractPatch = MicroTaskDescription.ExtractModpackPatch.Instance;
		MicroTaskDescription updateVersion = MicroTaskDescription.UpdateModpackVersion.Instance;
		factory.Setup(x => x.CreateExtractSevenZipExceptMicroTask("patch.7z", SkyrimRelativePath.Root,
				ProductTestSupport.CreateProduct().ModpackVersionFile, extractPatch))
			.Returns(Marker("extract without version"));
		factory.Setup(x => x.CreateExtractSevenZipEntryMicroTask("patch.7z", SkyrimRelativePath.Root,
				ProductTestSupport.CreateProduct().ModpackVersionFile, updateVersion))
			.Returns(Marker("extract version"));
		var installSteps = mocker.GetMock<IInstallStepsMicroTasksFactory>();
		installSteps.Setup(x => x.CreateInstallMicroTasks(patch.InstallSteps, null)).Returns([Marker("steps")]);
		mocker.GetMock<IServiceProvider>()
			.Setup(x => x.GetService(typeof(IInstallStepsMicroTasksFactory)))
			.Returns(installSteps.Object);
		var sut = mocker.CreateInstance<AppTaskProvider>();
		var task = await sut.CreateInstallPatchTask(patch, false, CancellationToken.None);

		// Act
		var microTasks = task.MicroTasksProvider(CreateMicroTasksProviderContext(mocker));

		// Assert
		Assert.That(microTasks.Select(x => x.Description.Value),
			Is.EqualTo(new object[] { new MicroTaskDescription.Custom("download"), new MicroTaskDescription.Custom("extract without version"), new MicroTaskDescription.Custom("steps"), new MicroTaskDescription.Custom("extract version") }));
	}

	[Test]
	public async Task CreateRunGameTask_FeedOffersNoModpack_ReturnsRunTaskWithoutInstallDependency()
	{
		// Arrange
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		OptionsStubs.Snapshot(mocker, new AppSettings { SkyrimDirectory = @"C:\Skyrim" });
		mocker.GetMock<IFeedProvider>()
			.Setup(x => x.GetModpackFeedItems(CancellationToken.None))
			.ReturnsAsync([]);
		var sut = mocker.CreateInstance<AppTaskProvider>();

		// Act
		var task = await sut.CreateRunGameTask(new GameLaunch("Mod Organizer", null), CancellationToken.None);

		// Assert
		Assert.That(task.Dependencies, Is.Empty);
	}

	[Test]
	public async Task CreateInstallModpackTask_WithPatch_AsksFirstAndInstallsComponentsAfterPatchForPatchedVersion()
	{
		// Arrange
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		OptionsStubs.Snapshot(mocker, new AppSettings { SkyrimDirectory = @"C:\Skyrim" });
		SetupComponentFeed(mocker);
		var prerequisites = mocker.GetMock<IPrerequisitesAppTaskProvider>();
		prerequisites.SetupGet(x => x.InstallVcRedistTasks).Returns([]);
		prerequisites.Setup(x => x.CreateInstallSevenZipTask(CancellationToken.None))
			.ReturnsAsync(new AppTask("InstallSevenZip", new AppTaskDescription.Custom("7-Zip"), _ => [], [], null));
		var feedProvider = mocker.GetMock<IFeedProvider>();
		feedProvider.Setup(x => x.GetSkyrimDownloadInfo(CancellationToken.None))
			.ReturnsAsync(new DownloadInfo { S3Path = "skyrim.7z" });
		var lite2 = new DownloadInfo { S3Path = "lite2.7z" };
		var liteItem = new ModFeedItem
		{
			Version = NuGetVersion.Parse("1.0.0"),
			ArchiveDownloadInfo = new DownloadInfo { S3Path = "lite1.7z" },
			InstallSteps = [new InstallStep { Actions = [new InstallAction.ExtractArchive(lite2, string.Empty)] }],
			SupportedModpackVersion = VersionRange.Parse("[3.2.1, 3.3.0)"),
		};
		var soloItem = new ModFeedItem
		{
			Version = NuGetVersion.Parse("2.0.0"),
			ArchiveDownloadInfo = new DownloadInfo { S3Path = "solo.7z" },
			SupportedModpackVersion = VersionRange.Parse("[3.2.0, 3.3.0)"),
		};
		var oldItem = new ModFeedItem
		{
			Version = NuGetVersion.Parse("1.0.0"),
			ArchiveDownloadInfo = new DownloadInfo { S3Path = "old.7z" },
			SupportedModpackVersion = VersionRange.Parse("[2.0.0, 3.0.0)"),
		};
		feedProvider.Setup(x => x.GetGraphPacks(CancellationToken.None))
			.ReturnsAsync(
			[
				CreateGraphPack("Lite", "4 GB", isDefault: true, liteItem),
				CreateGraphPack("Solo", "6 GB", isDefault: false, soloItem),
				CreateGraphPack("Full", "8 GB", isDefault: false, oldItem),
			]);
		var installAppTaskProvider = mocker.GetMock<IInstallAppTaskProvider>();
		installAppTaskProvider
			.Setup(x => x.CreateModInstallAppTask(It.Is<ModFeedData>(mod => mod.Name == "Lite"), liteItem.Version, CancellationToken.None))
			.ReturnsAsync(new AppTask("Install_Lite", new AppTaskDescription.Custom("Lite"), _ => [], [], null));
		installAppTaskProvider
			.Setup(x => x.CreateModInstallAppTask(It.Is<ModFeedData>(mod => mod.Name == "Solo"), soloItem.Version, CancellationToken.None))
			.ReturnsAsync(new AppTask("Install_Solo", new AppTaskDescription.Custom("Solo"), _ => [], [], null));
		var voicesExtraDownload = new DownloadInfo { S3Path = "voices-extra.7z" };
		var oldVoicesItem = CreateEnglishVoicesItem("1.0.0", "[3.2.0, 3.2.1)");
		var voicesItem = new ModFeedItem
		{
			Version = NuGetVersion.Parse("2.0.0"),
			ArchiveDownloadInfo = new DownloadInfo { S3Path = "voices-2.0.0.7z" },
			InstallSteps =
			[
				new InstallStep
				{
					Actions =
					[
						new InstallAction.ActivateMods(true, ["Other"]),
						new InstallAction.ExtractArchive(voicesExtraDownload, "Extra"),
					],
				},
			],
			SupportedModpackVersion = VersionRange.Parse("[3.2.1, )"),
		};
		var voicesMod = CreateEnglishVoicesMod(oldVoicesItem, voicesItem);
		var otherMod = new ModFeedData
		{
			Name = "Other", LogoPath = "", DescriptionPath = new LocalizedText { ["ru"] = "" },
			Items = [voicesItem],
		};
		feedProvider.Setup(x => x.GetModFeedDatas(CancellationToken.None)).ReturnsAsync([otherMod, voicesMod]);
		var voicesInstallTask = new AppTask("Install_Voices", new AppTaskDescription.Custom("Voices"), _ => [], [], null);
		installAppTaskProvider.Setup(x => x.CreateModInstallAppTask(voicesMod, voicesItem.Version, CancellationToken.None))
			.ReturnsAsync(voicesInstallTask);
		var sut = mocker.CreateInstance<AppTaskProvider>();
		var modpackItem = new ModpackFeedItem
		{
			InitialVersion = NuGetVersion.Parse("3.2.0"),
			Version = NuGetVersion.Parse("3.2.0"),
			DownloadInfo = new DownloadInfo { S3Path = "modpack.7z" },
		};
		var patch = new ModpackPatchFeedItem { Version = NuGetVersion.Parse("3.2.1"), DownloadInfo = new DownloadInfo { S3Path = "patch.7z" } };

		// Act
		var task = await sut.CreateInstallModpackTask(modpackItem, [patch], CancellationToken.None);

		// Assert
		var englishVoicesTask = task.Dependencies.Single();
		var patchTask = englishVoicesTask.Dependencies.Single();
		var modpackTask = patchTask.Dependencies[0];
		var selectComponentsTask = modpackTask.Dependencies[0];
		ModpackComponentsOffer? offer = null;
		mocker.GetMock<IMicroTasksFactory>()
			.Setup(x => x.CreateSelectModpackComponentsMicroTask(It.IsAny<ModpackComponentsOffer>()))
			.Callback<ModpackComponentsOffer>(x => offer = x)
			.Returns(mocker.GetMock<IAppMicroTask>().Object);
		selectComponentsTask.MicroTasksProvider(CreateMicroTasksProviderContext(mocker));
		Assert.Multiple(() =>
		{
			Assert.That(task.Id, Is.EqualTo("InstallModpackComponent_GraphPack"));
			Assert.That(englishVoicesTask.Id, Is.EqualTo("InstallModpackComponent_EnglishVoices"));
			Assert.That(patchTask.Id, Is.EqualTo("InstallModpackPatch_3.2.1"));
			Assert.That(modpackTask.Id, Is.EqualTo(AppTaskProvider.InstallModpackTaskId));
			Assert.That(selectComponentsTask.Id, Is.EqualTo("SelectModpackComponents"));
			Assert.That(
				new[] { task, englishVoicesTask, patchTask, modpackTask, selectComponentsTask }
					.Select(x => AppTaskProvider.IsModpackInstallationTask(x.Id)),
				Is.All.True);
			Assert.That(offer!.Components.Select(x => x.Definition.Id), Is.EqualTo(new[] { "EnglishVoices", "GraphPack" }));
			Assert.That(GraphPackOptions(offer!).Select(x => x.Name), Is.EqualTo(new[] { "Lite", "Solo", "Full" }));
			Assert.That(GraphPackOptions(offer!).Select(x => x.Recommendation), Is.EqualTo(new[] { "4 GB", "6 GB", "8 GB" }));
			Assert.That(GraphPackOptions(offer!).Select(x => x.InstallTask?.Id), Is.EqualTo(new[] { "Install_Lite", "Install_Solo", null }));
			Assert.That(GraphPackOptions(offer!).Select(x => x.IsDefault), Is.EqualTo(new[] { true, false, false }));
			Assert.That(GraphPackOptions(offer!).Select(x => x.Downloads), Is.EqualTo(new[]
			{
				new[] { liteItem.ArchiveDownloadInfo, lite2 },
				new[] { soloItem.ArchiveDownloadInfo },
				Array.Empty<DownloadInfo>(),
			}));
			Assert.That(VoicesOption(offer!).InstallTask, Is.SameAs(voicesInstallTask));
			Assert.That(VoicesOption(offer!).Downloads, Is.EqualTo(new[] { voicesItem.ArchiveDownloadInfo, voicesExtraDownload }));
		});
	}

	[Test]
	public async Task CreateInstallModpackTask_ComponentAnswers_MarksDeclinedComponentsUnneededOnlyOnceAnswered()
	{
		// Arrange
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		OptionsStubs.Snapshot(mocker, new AppSettings { SkyrimDirectory = @"C:\Skyrim" });
		SetupComponentFeed(mocker);
		var prerequisites = mocker.GetMock<IPrerequisitesAppTaskProvider>();
		prerequisites.SetupGet(x => x.InstallVcRedistTasks).Returns([]);
		prerequisites.Setup(x => x.CreateInstallSevenZipTask(CancellationToken.None))
			.ReturnsAsync(new AppTask("InstallSevenZip", new AppTaskDescription.Custom("7-Zip"), _ => [], [], null));
		var feedProvider = mocker.GetMock<IFeedProvider>();
		feedProvider.Setup(x => x.GetSkyrimDownloadInfo(CancellationToken.None))
			.ReturnsAsync(new DownloadInfo { S3Path = "skyrim.7z" });
		feedProvider.Setup(x => x.GetGraphPacks(CancellationToken.None)).ReturnsAsync([CreateUnavailableGraphPack()]);
		ModpackComponentsOffer? offer = null;
		mocker.GetMock<IMicroTasksFactory>()
			.Setup(x => x.CreateSelectModpackComponentsMicroTask(It.IsAny<ModpackComponentsOffer>()))
			.Callback<ModpackComponentsOffer>(x => offer = x)
			.Returns(mocker.GetMock<IAppMicroTask>().Object);
		SetupEnglishVoicesMod(mocker, "[3.0.0, )");
		var sut = mocker.CreateInstance<AppTaskProvider>();
		var modpackItem = new ModpackFeedItem
		{
			InitialVersion = NuGetVersion.Parse("3.2.0"),
			Version = NuGetVersion.Parse("3.2.0"),
			DownloadInfo = new DownloadInfo { S3Path = "modpack.7z" },
		};
		var graphPackTask = await sut.CreateInstallModpackTask(modpackItem, [], CancellationToken.None);
		var englishVoicesTask = graphPackTask.Dependencies.Single();
		var selectComponentsTask = englishVoicesTask.Dependencies.Single().Dependencies[0];
		selectComponentsTask.MicroTasksProvider(CreateMicroTasksProviderContext(mocker));
		var unneededBeforeAnswer = new[] { graphPackTask.IsUnneeded!(), englishVoicesTask.IsUnneeded!() };
		var choices = new ModpackComponentChoices(offer!, _ => 0);

		// Act
		choices.Apply(
		[
			new ChoiceStepSelection { Step = choices.Steps[0], SelectedOptions = [choices.Steps[0].Options[0]] },
			new ChoiceStepSelection { Step = choices.Steps[1], SelectedOptions = [choices.Steps[1].Options[^1]] },
		]);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(unneededBeforeAnswer, Is.EqualTo(new[] { false, false }));
			Assert.That(graphPackTask.IsUnneeded!(), Is.True);
			Assert.That(englishVoicesTask.IsUnneeded!(), Is.False);
		});
	}

	[Test]
	public async Task CreateInstallModpackTask_NoEnglishVoices_ChainsGraphPackDirectlyAfterModpack()
	{
		// Arrange
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		OptionsStubs.Snapshot(mocker, new AppSettings { SkyrimDirectory = @"C:\Skyrim" });
		SetupComponentFeed(mocker);
		var prerequisites = mocker.GetMock<IPrerequisitesAppTaskProvider>();
		prerequisites.SetupGet(x => x.InstallVcRedistTasks).Returns([]);
		prerequisites.Setup(x => x.CreateInstallSevenZipTask(CancellationToken.None))
			.ReturnsAsync(new AppTask("InstallSevenZip", new AppTaskDescription.Custom("7-Zip"), _ => [], [], null));
		var feedProvider = mocker.GetMock<IFeedProvider>();
		feedProvider.Setup(x => x.GetSkyrimDownloadInfo(CancellationToken.None))
			.ReturnsAsync(new DownloadInfo { S3Path = "skyrim.7z" });
		feedProvider.Setup(x => x.GetGraphPacks(CancellationToken.None)).ReturnsAsync([CreateUnavailableGraphPack()]);
		ModpackComponentsOffer? offer = null;
		mocker.GetMock<IMicroTasksFactory>()
			.Setup(x => x.CreateSelectModpackComponentsMicroTask(It.IsAny<ModpackComponentsOffer>()))
			.Callback<ModpackComponentsOffer>(x => offer = x)
			.Returns(mocker.GetMock<IAppMicroTask>().Object);
		mocker.GetMock<IFeedProvider>().Setup(x => x.GetModFeedDatas(CancellationToken.None)).ReturnsAsync((IReadOnlyList<ModFeedData>?)null);
		var sut = mocker.CreateInstance<AppTaskProvider>();
		var modpackItem = new ModpackFeedItem
		{
			InitialVersion = NuGetVersion.Parse("3.2.0"),
			Version = NuGetVersion.Parse("3.2.0"),
			DownloadInfo = new DownloadInfo { S3Path = "modpack.7z" },
		};

		// Act
		var graphPackTask = await sut.CreateInstallModpackTask(modpackItem, [], CancellationToken.None);

		// Assert
		var modpackTask = graphPackTask.Dependencies.Single();
		modpackTask.Dependencies[0].MicroTasksProvider(CreateMicroTasksProviderContext(mocker));
		Assert.Multiple(() =>
		{
			Assert.That(modpackTask.Id, Is.EqualTo(AppTaskProvider.InstallModpackTaskId));
			Assert.That(offer!.Components.Select(x => x.Definition.Kind), Does.Not.Contain(InstallComponentKind.Mod));
		});
	}

	[TestCase("3.1.9", "InstallModpackComponent_EnglishVoices")]
	[TestCase("3.1.10", "InstallModpackComponent_GraphPack")]
	public async Task CreateInstallModpackTask_ModpackVersion_OffersGraphPacksOnlyAfter319(string modpackVersion, string expectedLastTaskId)
	{
		// Arrange
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		OptionsStubs.Snapshot(mocker, new AppSettings { SkyrimDirectory = @"C:\Skyrim" });
		SetupComponentFeed(mocker);
		var prerequisites = mocker.GetMock<IPrerequisitesAppTaskProvider>();
		prerequisites.SetupGet(x => x.InstallVcRedistTasks).Returns([]);
		prerequisites.Setup(x => x.CreateInstallSevenZipTask(CancellationToken.None))
			.ReturnsAsync(new AppTask("InstallSevenZip", new AppTaskDescription.Custom("7-Zip"), _ => [], [], null));
		var feedProvider = mocker.GetMock<IFeedProvider>();
		feedProvider.Setup(x => x.GetSkyrimDownloadInfo(CancellationToken.None))
			.ReturnsAsync(new DownloadInfo { S3Path = "skyrim.7z" });
		feedProvider.Setup(x => x.GetGraphPacks(CancellationToken.None)).ReturnsAsync([CreateUnavailableGraphPack()]);
		ModpackComponentsOffer? offer = null;
		mocker.GetMock<IMicroTasksFactory>()
			.Setup(x => x.CreateSelectModpackComponentsMicroTask(It.IsAny<ModpackComponentsOffer>()))
			.Callback<ModpackComponentsOffer>(x => offer = x)
			.Returns(mocker.GetMock<IAppMicroTask>().Object);
		SetupEnglishVoicesMod(mocker, "[3.0.0, )");
		var sut = mocker.CreateInstance<AppTaskProvider>();
		var modpackItem = new ModpackFeedItem
		{
			InitialVersion = NuGetVersion.Parse(modpackVersion),
			Version = NuGetVersion.Parse(modpackVersion),
			DownloadInfo = new DownloadInfo { S3Path = "modpack.7z" },
		};

		// Act
		var task = await sut.CreateInstallModpackTask(modpackItem, [], CancellationToken.None);

		// Assert
		var modpackTask = task.Id == "InstallModpackComponent_GraphPack" ? task.Dependencies.Single().Dependencies.Single() : task.Dependencies.Single();
		modpackTask.Dependencies[0].MicroTasksProvider(CreateMicroTasksProviderContext(mocker));
		Assert.Multiple(() =>
		{
			Assert.That(task.Id, Is.EqualTo(expectedLastTaskId));
			Assert.That(GraphPackOptions(offer!).Count > 0, Is.EqualTo(expectedLastTaskId == "InstallModpackComponent_GraphPack"));
		});
	}

	[Test]
	public async Task CreateInstallModpackTask_Modpack319AndVoicesOnlyForNewerModpack_InstallsModpackWithoutAskingOrReadingGraphPacks()
	{
		// Arrange
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		OptionsStubs.Snapshot(mocker, new AppSettings { SkyrimDirectory = @"C:\Skyrim" });
		SetupComponentFeed(mocker);
		var prerequisites = mocker.GetMock<IPrerequisitesAppTaskProvider>();
		prerequisites.SetupGet(x => x.InstallVcRedistTasks).Returns([]);
		prerequisites.Setup(x => x.CreateInstallSevenZipTask(CancellationToken.None))
			.ReturnsAsync(new AppTask("InstallSevenZip", new AppTaskDescription.Custom("7-Zip"), _ => [], [], null));
		mocker.GetMock<IFeedProvider>().Setup(x => x.GetSkyrimDownloadInfo(CancellationToken.None))
			.ReturnsAsync(new DownloadInfo { S3Path = "skyrim.7z" });
		SetupEnglishVoicesMod(mocker, "[3.2.0, )");
		var sut = mocker.CreateInstance<AppTaskProvider>();
		var modpackItem = new ModpackFeedItem
		{
			InitialVersion = NuGetVersion.Parse("3.1.9"),
			Version = NuGetVersion.Parse("3.1.9"),
			DownloadInfo = new DownloadInfo { S3Path = "modpack.7z" },
		};

		// Act
		var task = await sut.CreateInstallModpackTask(modpackItem, [], CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(task.Id, Is.EqualTo(AppTaskProvider.InstallModpackTaskId));
			Assert.That(task.Dependencies.Select(x => x.Id), Does.Not.Contain("SelectModpackComponents"));
		});
	}

	[Test]
	public async Task CreateInstallModpackTask_NoGraphPacksInFeed_AsksOnlyAboutVoicesAndEndsWithVoices()
	{
		// Arrange
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		OptionsStubs.Snapshot(mocker, new AppSettings { SkyrimDirectory = @"C:\Skyrim" });
		SetupComponentFeed(mocker);
		var prerequisites = mocker.GetMock<IPrerequisitesAppTaskProvider>();
		prerequisites.SetupGet(x => x.InstallVcRedistTasks).Returns([]);
		prerequisites.Setup(x => x.CreateInstallSevenZipTask(CancellationToken.None))
			.ReturnsAsync(new AppTask("InstallSevenZip", new AppTaskDescription.Custom("7-Zip"), _ => [], [], null));
		var feedProvider = mocker.GetMock<IFeedProvider>();
		feedProvider.Setup(x => x.GetSkyrimDownloadInfo(CancellationToken.None))
			.ReturnsAsync(new DownloadInfo { S3Path = "skyrim.7z" });
		feedProvider.Setup(x => x.GetGraphPacks(CancellationToken.None)).ReturnsAsync([]);
		ModpackComponentsOffer? offer = null;
		mocker.GetMock<IMicroTasksFactory>()
			.Setup(x => x.CreateSelectModpackComponentsMicroTask(It.IsAny<ModpackComponentsOffer>()))
			.Callback<ModpackComponentsOffer>(x => offer = x)
			.Returns(mocker.GetMock<IAppMicroTask>().Object);
		SetupEnglishVoicesMod(mocker, "[3.0.0, )");
		var sut = mocker.CreateInstance<AppTaskProvider>();
		var modpackItem = new ModpackFeedItem
		{
			InitialVersion = NuGetVersion.Parse("3.2.0"),
			Version = NuGetVersion.Parse("3.2.0"),
			DownloadInfo = new DownloadInfo { S3Path = "modpack.7z" },
		};

		// Act
		var task = await sut.CreateInstallModpackTask(modpackItem, [], CancellationToken.None);

		// Assert
		task.Dependencies.Single().Dependencies[0].MicroTasksProvider(CreateMicroTasksProviderContext(mocker));
		var choices = new ModpackComponentChoices(offer!, _ => 0);
		Assert.Multiple(() =>
		{
			Assert.That(task.Id, Is.EqualTo("InstallModpackComponent_EnglishVoices"));
			Assert.That(GraphPackOptions(offer!), Is.Empty);
			Assert.That(choices.Steps.Select(x => x.Title.Value), Is.EqualTo(new[] { new LauncherKit.Core.Choices.ChoiceText.Content("Английская озвучка") }));
		});
	}

	[Test]
	public async Task CreateInstallModpackTask_NoGraphPacksInFeedAndNoVoices_InstallsModpackWithoutAsking()
	{
		// Arrange
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		OptionsStubs.Snapshot(mocker, new AppSettings { SkyrimDirectory = @"C:\Skyrim" });
		SetupComponentFeed(mocker);
		var prerequisites = mocker.GetMock<IPrerequisitesAppTaskProvider>();
		prerequisites.SetupGet(x => x.InstallVcRedistTasks).Returns([]);
		prerequisites.Setup(x => x.CreateInstallSevenZipTask(CancellationToken.None))
			.ReturnsAsync(new AppTask("InstallSevenZip", new AppTaskDescription.Custom("7-Zip"), _ => [], [], null));
		var feedProvider = mocker.GetMock<IFeedProvider>();
		feedProvider.Setup(x => x.GetSkyrimDownloadInfo(CancellationToken.None))
			.ReturnsAsync(new DownloadInfo { S3Path = "skyrim.7z" });
		feedProvider.Setup(x => x.GetGraphPacks(CancellationToken.None)).ReturnsAsync([]);
		mocker.GetMock<IFeedProvider>().Setup(x => x.GetModFeedDatas(CancellationToken.None)).ReturnsAsync((IReadOnlyList<ModFeedData>?)null);
		var sut = mocker.CreateInstance<AppTaskProvider>();
		var modpackItem = new ModpackFeedItem
		{
			InitialVersion = NuGetVersion.Parse("3.2.0"),
			Version = NuGetVersion.Parse("3.2.0"),
			DownloadInfo = new DownloadInfo { S3Path = "modpack.7z" },
		};

		// Act
		var task = await sut.CreateInstallModpackTask(modpackItem, [], CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(task.Id, Is.EqualTo(AppTaskProvider.InstallModpackTaskId));
			Assert.That(task.Dependencies.Select(x => x.Id), Does.Not.Contain("SelectModpackComponents"));
		});
	}

	[Test]
	public async Task CreateInstallModpackTask_DiscontinuedGraphPackWithoutSupportingVersion_OffersOnlyGraphPacksBeingUpdated()
	{
		// Arrange
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		OptionsStubs.Snapshot(mocker, new AppSettings { SkyrimDirectory = @"C:\Skyrim" });
		SetupComponentFeed(mocker);
		var prerequisites = mocker.GetMock<IPrerequisitesAppTaskProvider>();
		prerequisites.SetupGet(x => x.InstallVcRedistTasks).Returns([]);
		prerequisites.Setup(x => x.CreateInstallSevenZipTask(CancellationToken.None))
			.ReturnsAsync(new AppTask("InstallSevenZip", new AppTaskDescription.Custom("7-Zip"), _ => [], [], null));
		var feedProvider = mocker.GetMock<IFeedProvider>();
		feedProvider.Setup(x => x.GetSkyrimDownloadInfo(CancellationToken.None))
			.ReturnsAsync(new DownloadInfo { S3Path = "skyrim.7z" });
		feedProvider.Setup(x => x.GetGraphPacks(CancellationToken.None))
			.ReturnsAsync([CreateUnavailableGraphPack("Discontinued", isDiscontinued: true), CreateUnavailableGraphPack("Updating", isDiscontinued: false)]);
		ModpackComponentsOffer? offer = null;
		mocker.GetMock<IMicroTasksFactory>()
			.Setup(x => x.CreateSelectModpackComponentsMicroTask(It.IsAny<ModpackComponentsOffer>()))
			.Callback<ModpackComponentsOffer>(x => offer = x)
			.Returns(mocker.GetMock<IAppMicroTask>().Object);
		mocker.GetMock<IFeedProvider>().Setup(x => x.GetModFeedDatas(CancellationToken.None)).ReturnsAsync((IReadOnlyList<ModFeedData>?)null);
		var sut = mocker.CreateInstance<AppTaskProvider>();
		var modpackItem = new ModpackFeedItem
		{
			InitialVersion = NuGetVersion.Parse("3.2.0"),
			Version = NuGetVersion.Parse("3.2.0"),
			DownloadInfo = new DownloadInfo { S3Path = "modpack.7z" },
		};

		// Act
		var task = await sut.CreateInstallModpackTask(modpackItem, [], CancellationToken.None);

		// Assert
		task.Dependencies.Single().Dependencies[0].MicroTasksProvider(CreateMicroTasksProviderContext(mocker));
		Assert.Multiple(() =>
		{
			Assert.That(task.Id, Is.EqualTo("InstallModpackComponent_GraphPack"));
			Assert.That(GraphPackOptions(offer!).Select(x => x.Name), Is.EqualTo(new[] { "Updating" }));
		});
	}

	[Test]
	public async Task CreateInstallModpackTask_OnlyDiscontinuedGraphPackWithoutSupportingVersionAndNoVoices_InstallsModpackWithoutAsking()
	{
		// Arrange
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		OptionsStubs.Snapshot(mocker, new AppSettings { SkyrimDirectory = @"C:\Skyrim" });
		SetupComponentFeed(mocker);
		var prerequisites = mocker.GetMock<IPrerequisitesAppTaskProvider>();
		prerequisites.SetupGet(x => x.InstallVcRedistTasks).Returns([]);
		prerequisites.Setup(x => x.CreateInstallSevenZipTask(CancellationToken.None))
			.ReturnsAsync(new AppTask("InstallSevenZip", new AppTaskDescription.Custom("7-Zip"), _ => [], [], null));
		var feedProvider = mocker.GetMock<IFeedProvider>();
		feedProvider.Setup(x => x.GetSkyrimDownloadInfo(CancellationToken.None))
			.ReturnsAsync(new DownloadInfo { S3Path = "skyrim.7z" });
		feedProvider.Setup(x => x.GetGraphPacks(CancellationToken.None))
			.ReturnsAsync([CreateUnavailableGraphPack("Discontinued", isDiscontinued: true)]);
		mocker.GetMock<IFeedProvider>().Setup(x => x.GetModFeedDatas(CancellationToken.None)).ReturnsAsync((IReadOnlyList<ModFeedData>?)null);
		var sut = mocker.CreateInstance<AppTaskProvider>();
		var modpackItem = new ModpackFeedItem
		{
			InitialVersion = NuGetVersion.Parse("3.2.0"),
			Version = NuGetVersion.Parse("3.2.0"),
			DownloadInfo = new DownloadInfo { S3Path = "modpack.7z" },
		};

		// Act
		var task = await sut.CreateInstallModpackTask(modpackItem, [], CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(task.Id, Is.EqualTo(AppTaskProvider.InstallModpackTaskId));
			Assert.That(task.Dependencies.Select(x => x.Id), Does.Not.Contain("SelectModpackComponents"));
		});
	}

	[Test]
	public async Task CreateInstallModpackTask_FeedPrerequisite_DownloadsAndRunsStoredExecutableWithDeclaredArguments()
	{
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		OptionsStubs.Snapshot(mocker, new AppSettings { SkyrimDirectory = @"C:\Skyrim" });
		SetupComponentFeed(mocker);
		var feed = mocker.GetMock<IFeedProvider>();
		feed.Setup(x => x.GetInstallComponents(CancellationToken.None)).ReturnsAsync([]);
		feed.Setup(x => x.GetSkyrimDownloadInfo(CancellationToken.None))
			.ReturnsAsync(new DownloadInfo { S3Path = "skyrim.7z" });
		var prerequisite = new Prerequisite
		{
			Id = "CustomRuntime",
			DownloadInfo = new DownloadInfo { S3Path = "Prerequisites/runtime.exe" },
			Arguments = "/quiet /custom",
		};
		feed.Setup(x => x.GetPrerequisites(CancellationToken.None)).ReturnsAsync([prerequisite]);
		var prerequisites = mocker.GetMock<IPrerequisitesAppTaskProvider>();
		prerequisites.SetupGet(x => x.InstallVcRedistTasks).Returns([]);
		prerequisites.Setup(x => x.CreateInstallSevenZipTask(CancellationToken.None))
			.ReturnsAsync(new AppTask("InstallSevenZip", new AppTaskDescription.Custom("7-Zip"), _ => [], [], null));
		mocker.GetMock<IFileStorageService>().Setup(x => x.GetFilePath("runtime.exe"))
			.Returns(@"C:\Downloads\runtime.exe");
		var factory = mocker.GetMock<IMicroTasksFactory>();
		factory.Setup(x => x.CreateDownloadFileMicroTask(It.Is<DownloadFileSource>(source =>
				ReferenceEquals(source.Value, prerequisite.DownloadInfo))))
			.Returns(Marker("download prerequisite"));
		factory.Setup(x => x.CreateDownloadFileMicroTask(It.Is<DownloadFileSource>(source =>
				((DownloadInfo)source.Value!).S3Path == "modpack.7z")))
			.Returns(Marker("download modpack"));
		MicroTaskDescription deletePreviousVersion = MicroTaskDescription.DeletePreviousVersion.Instance;
		MicroTaskDescription deleteBackups = MicroTaskDescription.DeleteBackups.Instance;
		MicroTaskDescription? extractModpack = MicroTaskDescription.ExtractModpack.Instance;
		MicroTaskDescription installPrerequisite = new MicroTaskDescription.InstallPrerequisite("CustomRuntime");
		factory.Setup(x => x.CreateDeleteFileSystemItemsMicroTask(
				It.IsAny<IReadOnlyCollection<SkyrimRelativePath>>(), true, deletePreviousVersion))
			.Returns(Marker("delete old"));
		factory.Setup(x => x.CreateDelegateMicroTask(
				It.IsAny<Func<R3.ISubject<MicroTaskStep>, CancellationToken, ValueTask>>(), deleteBackups))
			.Returns(Marker("delete backups"));
		mocker.GetMock<IServiceProvider>().Setup(x => x.GetService(typeof(IInstallBackupService)))
			.Returns(mocker.GetMock<IInstallBackupService>().Object);
		factory.Setup(x => x.CreateExtractSevenZipMicroTask("modpack.7z", SkyrimRelativePath.Root, extractModpack))
			.Returns(Marker("extract modpack"));
		Func<ProcessStartInfo>? process = null;
		factory.Setup(x => x.CreateRunProcessMicroTask(It.IsAny<Func<ProcessStartInfo>>(), false, installPrerequisite))
			.Callback<Func<ProcessStartInfo>, bool, MicroTaskDescription>((provider, _, _) => process = provider)
			.Returns(Marker("run prerequisite"));
		var sut = mocker.CreateInstance<AppTaskProvider>();
		var task = await sut.CreateInstallModpackTask(new ModpackFeedItem
		{
			InitialVersion = NuGetVersion.Parse("2.0.0"),
			Version = NuGetVersion.Parse("2.0.0"),
			DownloadInfo = new DownloadInfo { S3Path = "modpack.7z" },
		}, [], CancellationToken.None);

		var microTasks = task.MicroTasksProvider(CreateMicroTasksProviderContext(mocker));
		var startInfo = process!();

		Assert.Multiple(() =>
		{
			Assert.That(microTasks.Select(x => x.Description.Value), Is.EqualTo(new object[]
			{
				new MicroTaskDescription.Custom("download modpack"), new MicroTaskDescription.Custom("delete old"), new MicroTaskDescription.Custom("delete backups"), new MicroTaskDescription.Custom("extract modpack"),
				new MicroTaskDescription.Custom("download prerequisite"), new MicroTaskDescription.Custom("run prerequisite"),
			}));
			Assert.That(startInfo.FileName, Is.EqualTo(@"C:\Downloads\runtime.exe"));
			Assert.That(startInfo.Arguments, Is.EqualTo("/quiet /custom"));
			Assert.That(startInfo.UseShellExecute, Is.True);
		});
	}

	private static IReadOnlyList<GraphPackComponent> GraphPackOptions(ModpackComponentsOffer offer) =>
		offer.Components.SingleOrDefault(x => x.Definition.Kind == InstallComponentKind.GraphPack)?.Options ?? [];

	private static GraphPackComponent VoicesOption(ModpackComponentsOffer offer) =>
		offer.Components.Single(x => x.Definition.Id == "EnglishVoices").Options.Single();

	private static void SetupComponentFeed(AutoMocker mocker)
	{
		SetupDeclarations(mocker, "0.0.0");
		var feed = mocker.GetMock<IFeedProvider>();
		feed.Setup(x => x.GetPrerequisites(CancellationToken.None)).ReturnsAsync([]);
		feed.Setup(x => x.GetModFeedDatas(CancellationToken.None)).ReturnsAsync((IReadOnlyList<ModFeedData>?)null);
		feed.Setup(x => x.GetGraphPacks(CancellationToken.None)).ReturnsAsync([]);
	}

	private static void SetupDeclarations(AutoMocker mocker, string voicesRange)
	{
		// Deliberately list graph first: runtime must follow Order, not JSON list order.
		mocker.GetMock<IFeedProvider>().Setup(x => x.GetInstallComponents(CancellationToken.None)).ReturnsAsync(
		[
			new InstallComponent
			{
				Id = "GraphPack", Kind = InstallComponentKind.GraphPack,
				Title = new LocalizedText { ["ru"] = "Графпак" },
				Description = new LocalizedText { ["ru"] = "Установить графпак вместе со сборкой?" },
				SupportedModpackVersion = VersionRange.Parse("(3.1.9,)"), Order = 1,
			},
			new InstallComponent
			{
				Id = "EnglishVoices", Kind = InstallComponentKind.Mod, ModName = "Voices",
				Title = new LocalizedText { ["ru"] = "Английская озвучка" },
				Description = new LocalizedText { ["ru"] = "Установить английскую озвучку вместе со сборкой?" },
				SupportedModpackVersion = VersionRange.Parse(voicesRange), Order = 0,
			},
		]);
	}

	private static GraphPackFeedData CreateGraphPack(string name, string recommendation, bool isDefault, ModFeedItem item,
		bool isDiscontinued = false) => new()
	{
		Name = name,
		LogoPath = $"{name}.jpg",
		DescriptionPath = new LocalizedText { ["ru"] = $"{name}.md" },
		Recommendation = new LocalizedText { ["ru"] = recommendation },
		IsDefault = isDefault,
		IsDiscontinued = isDiscontinued,
		Items = [item],
	};

	private static GraphPackFeedData CreateUnavailableGraphPack(string name = "Old", bool isDiscontinued = false) =>
		CreateGraphPack(name, "", isDefault: true, new ModFeedItem
		{
			Version = NuGetVersion.Parse("1.0.0"),
			ArchiveDownloadInfo = new DownloadInfo { S3Path = "old.7z" },
			SupportedModpackVersion = VersionRange.Parse("[2.0.0, 3.0.0)"),
		}, isDiscontinued);

	private static MicroTasksProviderContext CreateMicroTasksProviderContext(AutoMocker mocker) =>
		new(mocker.GetMock<IMicroTasksFactory>().Object, mocker.GetMock<IServiceProvider>().Object);

	/// <summary>A microtask that does nothing, told apart by its name.</summary>
	private static IAppMicroTask Marker(string name) => new DelegateMicroTask(new MicroTaskDescription.Custom(name), (_, _) => ValueTask.CompletedTask);

	/// <summary>Makes the feed list the English voices mod with one version supporting <paramref name="supportedModpackVersion"/>.</summary>
	private static void SetupEnglishVoicesMod(AutoMocker mocker, string supportedModpackVersion)
	{
		SetupDeclarations(mocker, supportedModpackVersion);
		var voicesMod = CreateEnglishVoicesMod(CreateEnglishVoicesItem("1.0.0", supportedModpackVersion));
		mocker.GetMock<IFeedProvider>().Setup(x => x.GetModFeedDatas(CancellationToken.None)).ReturnsAsync([voicesMod]);
		mocker.GetMock<IInstallAppTaskProvider>()
			.Setup(x => x.CreateModInstallAppTask(voicesMod, voicesMod.Items[0].Version, CancellationToken.None))
			.ReturnsAsync(new AppTask("Install_Voices", new AppTaskDescription.Custom("Voices"), _ => [], [], null));
	}

	private static ModFeedData CreateEnglishVoicesMod(params ModFeedItem[] items) => new()
	{
		Name = "Voices",
		LogoPath = "voices.jpg",
		DescriptionPath = new LocalizedText { ["ru"] = "voices.md" },
		Items = items,
	};

	private static ModFeedItem CreateEnglishVoicesItem(string version, string supportedModpackVersion) => new()
	{
		Version = NuGetVersion.Parse(version),
		ArchiveDownloadInfo = new DownloadInfo { S3Path = $"voices-{version}.7z" },
		SupportedModpackVersion = VersionRange.Parse(supportedModpackVersion),
	};
}
