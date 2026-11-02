using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Versioning;
using R3;
using LauncherKit.Core.AppTasks.InstallSteps;
using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.Core.Backups;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.Install;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.Tests.AppTasks.InstallSteps;

[TestFixture]
public sealed class InstallStepsMicroTasksFactoryTests
{
	private static readonly InstallBackupId BackupId = InstallBackupId.ForModVersion("Mod", NuGetVersion.Parse("1.0.0"));

	[Test]
	public async Task CreateInstallMicroTasks_Steps_RunsActionsInOrderAndDownloadsArchivesBeforeUsingThem()
	{
		// Arrange
		var calls = new List<string>();
		var mocker = CreateMocker(calls);
		mocker.GetMock<IModOrganizerService>().Setup(x => x.ActivateMod("Hub", false, true, CancellationToken.None))
			.Callback(() => calls.Add("deactivate Hub"))
			.Returns(Task.CompletedTask);
		mocker.GetMock<IModOrganizerService>().Setup(x => x.ChangeProfile("Graph pack", CancellationToken.None))
			.Callback(() => calls.Add("profile Graph pack"))
			.Returns(Task.CompletedTask);
		var sut = mocker.CreateInstance<InstallStepsMicroTasksFactory>();
		InstallStep[] steps =
		[
			new() { Actions = [new InstallAction.InstallMod(new DownloadInfo { S3Path = "Mods/extra.7z" }, "Extra",
				new ModPlacement.NextToMod("Base", ModListSide.Below))] },
			new()
			{
				Actions =
				[
					new InstallAction.ActivateMods(false, ["Hub"]),
					new InstallAction.ExtractArchive(new DownloadInfo { S3Path = "Mods/files.7z" }, "Data"),
					new InstallAction.MergePlugins(new DownloadInfo { S3Path = "Mods/extra plugins.txt" }),
					new InstallAction.ChangeProfile("Graph pack"),
					new InstallAction.RemoveFileSystemItem([@"Data\old.esp", "Cache"]),
				],
			},
		];

		// Act
		await Run(sut.CreateInstallMicroTasks(steps, null));

		// Assert
		Assert.That(calls, Is.EqualTo(new[]
		{
			"download extra.7z", "install Extra from extra.7z NextToMod { ModName = Base, Side = Below }", "deactivate Hub", "download files.7z",
			"extract files.7z to Data", "download extra plugins.txt", "merge plugins from extra plugins.txt", "profile Graph pack",
			@"delete Data\old.esp, Cache",
		}));
	}

	[TestCase(true)]
	[TestCase(false)]
	public async Task CreateInstallMicroTasks_RollbackableSteps_BacksUpDeletedAndOverwrittenFiles(bool failOnFailedDelete)
	{
		// Arrange
		var calls = new List<string>();
		var mocker = CreateMocker(calls);
		mocker.GetMock<IFileStorageService>().Setup(x => x.GetFilePath("files.7z")).Returns(@"C:\Downloads\files.7z");
		var backups = mocker.GetMock<IInstallBackupService>();
		backups.Setup(x => x.DeleteWithBackup(BackupId.ForAction(1, 0), It.IsAny<SkyrimRelativePath>(), CancellationToken.None))
			.Returns<InstallActionBackup, SkyrimRelativePath, CancellationToken>((_, path, _) =>
			{
				calls.Add($"delete {path.Path} with backup");
				return path.Path == "locked"
					? new FileSystemUtils.SafeDeleteResult.Error(new IOException("locked"))
					: FileSystemUtils.SafeDeleteResult.Success.Instance;
			});
		backups.Setup(x => x.BackupBeforeExtraction(BackupId.ForAction(1, 1), @"C:\Downloads\files.7z",
				It.Is<SkyrimRelativePath>(path => path.Path == "Data"), CancellationToken.None))
			.Callback(() => calls.Add("back up before extracting files.7z"))
			.Returns(Task.CompletedTask);
		backups.Setup(x => x.StartTracking(BackupId)).Callback(() => calls.Add("start tracking"));
		var sut = mocker.CreateInstance<InstallStepsMicroTasksFactory>();
		InstallStep[] steps =
		[
			new() { Actions = [new InstallAction.RemoveFileSystemItem(["cache"])] },
			new()
			{
				Actions =
				[
					new InstallAction.RemoveFileSystemItem(["old.esp", "locked", "next.esp"], failOnFailedDelete),
					new InstallAction.ExtractArchive(new DownloadInfo { S3Path = "Mods/files.7z" }, "Data"),
					new InstallAction.MergePlugins(new DownloadInfo { S3Path = "Mods/plugins.txt" }),
				],
				CanRollback = true,
			},
		];

		// Act
		async Task Act() => await Run(sut.CreateInstallMicroTasks(steps, BackupId));

		// Assert
		if (failOnFailedDelete)
		{
			Assert.Multiple(() =>
			{
				Assert.That(Act, Throws.TypeOf<LauncherKit.Core.Exceptions.FileSystemItemDeletionException>());
				Assert.That(calls, Is.EqualTo(new[]
				{
					"start tracking", "delete cache", "delete old.esp with backup", "delete locked with backup",
				}));
			});
		}
		else
		{
			await Act();
			Assert.That(calls, Is.EqualTo(new[]
			{
				"start tracking", "delete cache", "delete old.esp with backup", "delete locked with backup",
				"delete next.esp with backup",
				"download files.7z", "back up before extracting files.7z", "extract files.7z to Data", "download plugins.txt",
				$"merge plugins from plugins.txt with backup {BackupId.ForAction(1, 2).RelativePath}",
			}));
		}
	}

	[Test]
	public async Task CreateInstallMicroTasks_ConditionalSteps_RunsOnlyStepsWhoseConditionMatches()
	{
		// Arrange
		var calls = new List<string>();
		var mocker = CreateMocker(calls);
		mocker.GetMock<IModOrganizerService>().Setup(x => x.ActivateMod("A", true, true, CancellationToken.None))
			.Callback(() => calls.Add("activate A"))
			.Returns(Task.CompletedTask);
		var sut = mocker.CreateInstance<InstallStepsMicroTasksFactory>();
		InstallStep[] steps =
		[
			new() { Actions = [new InstallAction.SelectRequiredMod(["A", "B"])] },
			new()
			{
				Actions = [new InstallAction.ActivateMods(true, ["A"])],
				Condition = new InstallCondition { StepIndex = 0, Key = "SelectedMod", Operator = CompareOperator.Equal, Value = "A" },
			},
			new()
			{
				Actions = [new InstallAction.ActivateMods(true, ["B"])],
				Condition = new InstallCondition { StepIndex = 0, Key = "SelectedMod", Operator = CompareOperator.Equal, Value = "B" },
			},
		];

		// Act
		await Run(sut.CreateInstallMicroTasks(steps, null));

		// Assert
		Assert.That(calls, Is.EqualTo(new[] { "select A", "activate A" }));
	}

	[Test]
	public async Task CreateInstallMicroTasks_RollbackableConditionalSteps_RecordsOnlyActionsThatRan()
	{
		// Arrange
		var calls = new List<string>();
		var mocker = CreateMocker(calls);
		var modOrganizer = mocker.GetMock<IModOrganizerService>();
		modOrganizer.Setup(x => x.ActivateMod("A", true, true, CancellationToken.None))
			.Callback(() => calls.Add("activate A"))
			.Returns(Task.CompletedTask);
		modOrganizer.Setup(x => x.ChangeProfile("Graph pack", CancellationToken.None))
			.Callback(() => calls.Add("profile Graph pack"))
			.Returns(Task.CompletedTask);
		var backups = mocker.GetMock<IInstallBackupService>();
		backups.Setup(x => x.StartTracking(BackupId)).Callback(() => calls.Add("start tracking"));
		backups.Setup(x => x.MarkApplied(It.IsAny<InstallActionBackup>()))
			.Callback((InstallActionBackup backup) => calls.Add($"applied {backup.StepIndex}.{backup.ActionIndex}"));
		var sut = mocker.CreateInstance<InstallStepsMicroTasksFactory>();
		InstallStep[] steps =
		[
			new() { Actions = [new InstallAction.SelectRequiredMod(["A", "B"])] },
			new()
			{
				Actions =
				[
					new InstallAction.InstallMod(new DownloadInfo { S3Path = "Mods/extra.7z" }, "Extra"),
					new InstallAction.ActivateMods(true, ["A"]),
				],
				Condition = new InstallCondition { StepIndex = 0, Key = "SelectedMod", Operator = CompareOperator.Equal, Value = "A" },
				CanRollback = true,
			},
			new()
			{
				Actions = [new InstallAction.ActivatePlugins(true, ["b.esp"])],
				Condition = new InstallCondition { StepIndex = 0, Key = "SelectedMod", Operator = CompareOperator.Equal, Value = "B" },
				CanRollback = true,
			},
			new() { Actions = [new InstallAction.ChangeProfile("Graph pack")], CanRollback = true },
		];

		// Act
		await Run(sut.CreateInstallMicroTasks(steps, BackupId));

		// Assert
		Assert.That(calls, Is.EqualTo(new[]
		{
			"start tracking", "select A", "download extra.7z", "applied 1.0", "install Extra from extra.7z", "applied 1.1",
			"activate A", "applied 3.0", "profile Graph pack",
		}));
	}

	[Test]
	public async Task CreateRollbackMicroTasks_UntrackedRollbackableSteps_UndoesTheirActionsInReverseOrder()
	{
		// Arrange
		var calls = new List<string>();
		var mocker = CreateMocker(calls);
		var modOrganizer = mocker.GetMock<IModOrganizerService>();
		modOrganizer.Setup(x => x.IsModInstalled("Hub")).Returns(true);
		modOrganizer.Setup(x => x.IsModInstalled("Removed")).Returns(false);
		modOrganizer.Setup(x => x.IsModInstalled("Extra")).Returns(true);
		modOrganizer.Setup(x => x.IsModInstalled("Skipped")).Returns(false);
		modOrganizer.Setup(x => x.UninstallMod("Extra", CancellationToken.None))
			.Callback(() => calls.Add("uninstall Extra"))
			.Returns(Task.CompletedTask);
		modOrganizer.Setup(x => x.ActivatePlugins(new[] { "a.esp" }, false, CancellationToken.None))
			.Callback(() => calls.Add("deactivate a.esp"))
			.Returns(Task.CompletedTask);
		modOrganizer.Setup(x => x.ActivateMod("Hub", true, true, CancellationToken.None))
			.Callback(() => calls.Add("activate Hub"))
			.Returns(Task.CompletedTask);
		modOrganizer.Setup(x => x.ChangeProfile("Example", CancellationToken.None))
			.Callback(() => calls.Add("profile Example"))
			.Returns(Task.CompletedTask);
		var backups = mocker.GetMock<IInstallBackupService>();
		// Installed by a launcher that did not record which actions ran.
		backups.Setup(x => x.IsTracked(BackupId)).Returns(false);
		backups.Setup(x => x.Delete(It.IsAny<InstallActionBackup>()));
		backups.Setup(x => x.Restore(BackupId.ForAction(1, 1), CancellationToken.None))
			.Callback(() => calls.Add("restore cache"))
			.ReturnsAsync(true);
		modOrganizer.Setup(x => x.RevertPluginsMerge(BackupId.ForAction(1, 2), CancellationToken.None))
			.Callback(() => calls.Add("revert plugins merge"))
			.ReturnsAsync(true);
		// Its step's condition skipped the extraction, so it has no backup.
		backups.Setup(x => x.Restore(BackupId.ForAction(2, 2), CancellationToken.None))
			.Callback(() => calls.Add("restore files.7z"))
			.ReturnsAsync(false);
		var sut = mocker.CreateInstance<InstallStepsMicroTasksFactory>();
		InstallStep[] steps =
		[
			new() { Actions = [new InstallAction.ActivateMods(true, ["NotRolledBack"])] },
			new()
			{
				Actions =
				[
					new InstallAction.ChangeProfile("Graph pack"),
					new InstallAction.RemoveFileSystemItem(["cache"]),
					new InstallAction.MergePlugins(new DownloadInfo { S3Path = "Mods/plugins.txt" }),
					new InstallAction.ActivateMods(false, ["Hub", "Removed"]),
					new InstallAction.ActivatePlugins(true, ["a.esp"]),
				],
				CanRollback = true,
			},
			new()
			{
				Actions =
				[
					new InstallAction.InstallMod(new DownloadInfo { S3Path = "Mods/extra.7z" }, "Extra"),
					new InstallAction.InstallMod(new DownloadInfo { S3Path = "Mods/skipped.7z" }, "Skipped"),
					new InstallAction.ExtractArchive(new DownloadInfo { S3Path = "Mods/files.7z" }, "Data"),
				],
				CanRollback = true,
			},
		];

		// Act
		await Run(sut.CreateRollbackMicroTasks(steps, BackupId));

		// Assert
		Assert.That(calls, Is.EqualTo(new[]
		{
			"restore files.7z", "uninstall Extra", "deactivate a.esp", "activate Hub", "revert plugins merge", "restore cache",
			"profile Example",
		}));
	}

	[Test]
	public async Task CreateRollbackMicroTasks_TrackedSteps_UndoesOnlyActionsThatRanAndForgetsThem()
	{
		// Arrange
		var calls = new List<string>();
		var mocker = CreateMocker(calls);
		var modOrganizer = mocker.GetMock<IModOrganizerService>();
		modOrganizer.Setup(x => x.IsModInstalled("Hub")).Returns(true);
		modOrganizer.Setup(x => x.ActivateMod("Hub", true, true, CancellationToken.None))
			.Callback(() => calls.Add("activate Hub"))
			.Returns(Task.CompletedTask);
		var backups = mocker.GetMock<IInstallBackupService>();
		backups.Setup(x => x.IsTracked(BackupId)).Returns(true);
		backups.Setup(x => x.IsApplied(It.IsAny<InstallActionBackup>()))
			.Returns((InstallActionBackup backup) => backup == BackupId.ForAction(0, 1));
		backups.Setup(x => x.Delete(BackupId.ForAction(0, 1))).Callback(() => calls.Add("forget 0.1"));
		var sut = mocker.CreateInstance<InstallStepsMicroTasksFactory>();
		InstallStep[] steps =
		[
			new()
			{
				Actions =
				[
					new InstallAction.ChangeProfile("Graph pack"),
					new InstallAction.ActivateMods(false, ["Hub"]),
					new InstallAction.ActivatePlugins(true, ["a.esp"]),
					new InstallAction.InstallMod(new DownloadInfo { S3Path = "Mods/extra.7z" }, "Extra"),
				],
				CanRollback = true,
			},
		];

		// Act
		await Run(sut.CreateRollbackMicroTasks(steps, BackupId));

		// Assert
		Assert.That(calls, Is.EqualTo(new[] { "activate Hub", "forget 0.1" }));
	}

	/// <summary>
	/// A mocker whose microtask factory creates runnable microtasks: delegate microtasks run their delegate, while
	/// downloads, mod installations, archive extractions, plugin merges and required mod selections (which selects the
	/// first mod) are recorded in <paramref name="calls"/>.
	/// </summary>
	private static AutoMocker CreateMocker(List<string> calls)
	{
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		mocker.Use<ILogger<InstallStepsMicroTasksFactory>>(NullLogger<InstallStepsMicroTasksFactory>.Instance);
		var factory = mocker.GetMock<IMicroTasksFactory>();
		factory
			.Setup(x => x.CreateDelegateMicroTask(
				It.IsAny<Func<ISubject<MicroTaskStep>, CancellationToken, ValueTask>>(), It.IsAny<MicroTaskDescription>()))
			.Returns<Func<ISubject<MicroTaskStep>, CancellationToken, ValueTask>, MicroTaskDescription>(
				(func, description) => new DelegateMicroTask(description, func));
		factory
			.Setup(x => x.CreateDownloadFileMicroTask(It.IsAny<DownloadFileSource>()))
			.Returns<DownloadFileSource>(source => Recording(calls, $"download {((DownloadInfo)source.Value!).FileName}"));
		factory
			.Setup(x => x.CreateInstallModMicroTask(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ModPlacement?>()))
			.Returns<string, string, ModPlacement?>((modName, archiveName, placement) =>
				Recording(calls, $"install {modName} from {archiveName}{(placement != null ? $" {placement}" : "")}"));
		factory
			.Setup(x => x.CreateExtractSevenZipMicroTask(It.IsAny<string>(), It.IsAny<SkyrimRelativePath>(), It.IsAny<MicroTaskDescription?>()))
			.Returns<string, SkyrimRelativePath, MicroTaskDescription?>((archiveName, destination, _) =>
				Recording(calls, $"extract {archiveName} to {destination.Path}"));
		factory
			.Setup(x => x.CreateDeleteFileSystemItemsMicroTask(It.IsAny<IReadOnlyCollection<SkyrimRelativePath>>(), true, It.IsAny<MicroTaskDescription>()))
			.Returns<IReadOnlyCollection<SkyrimRelativePath>, bool, MicroTaskDescription>((paths, _, _) =>
				Recording(calls, $"delete {string.Join(", ", paths.Select(x => x.Path))}"));
		factory
			.Setup(x => x.CreateMergePluginsMicroTask(It.IsAny<string>(), It.IsAny<InstallActionBackup?>()))
			.Returns<string, InstallActionBackup?>((fileName, backup) => Recording(calls,
				$"merge plugins from {fileName}{(backup != null ? $" with backup {backup.RelativePath}" : "")}"));
		factory
			.Setup(x => x.CreateSelectRequiredModMicroTask(It.IsAny<InstallStepContext>(), It.IsAny<IReadOnlyList<string>>()))
			.Returns<InstallStepContext, IReadOnlyList<string>>((context, mods) => new DelegateMicroTask(new MicroTaskDescription.Custom("select"), (_, _) =>
			{
				calls.Add($"select {mods[0]}");
				context.SetValue("SelectedMod", mods[0]);
				return ValueTask.CompletedTask;
			}));
		return mocker;
	}

	private static DelegateMicroTask Recording(List<string> calls, string call) =>
		new(new MicroTaskDescription.Custom(call), (_, _) =>
		{
			calls.Add(call);
			return ValueTask.CompletedTask;
		});

	private static async Task Run(IReadOnlyList<IAppMicroTask> microTasks)
	{
		using var subject = new Subject<MicroTaskStep>();
		foreach (var microTask in microTasks)
		{
			await microTask.Execute(subject, CancellationToken.None);
		}
	}
}
