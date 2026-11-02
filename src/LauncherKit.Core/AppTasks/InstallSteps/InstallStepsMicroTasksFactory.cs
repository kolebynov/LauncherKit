using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.Core.Backups;
using LauncherKit.Core.Extensions;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.Install;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.AppTasks.InstallSteps;

public sealed class InstallStepsMicroTasksFactory : IInstallStepsMicroTasksFactory
{
	private readonly IMicroTasksFactory _microTasksFactory;
	private readonly IModOrganizerService _modOrganizerService;
	private readonly IInstallBackupService _installBackupService;
	private readonly IFileStorageService _fileStorageService;
	private readonly ILogger<InstallStepsMicroTasksFactory> _logger;
	private readonly LauncherProductOptions _product;

	public InstallStepsMicroTasksFactory(IMicroTasksFactory microTasksFactory, IModOrganizerService modOrganizerService,
		IInstallBackupService installBackupService, IFileStorageService fileStorageService,
		ILogger<InstallStepsMicroTasksFactory> logger, IOptions<LauncherProductOptions> product)
	{
		_microTasksFactory = microTasksFactory;
		_modOrganizerService = modOrganizerService;
		_installBackupService = installBackupService;
		_fileStorageService = fileStorageService;
		_logger = logger;
		_product = product.Value;
	}

	public IReadOnlyList<IAppMicroTask> CreateInstallMicroTasks(IReadOnlyList<InstallStep> steps, InstallBackupId? backupId)
	{
		var context = new InstallContext();
		var microTasks = steps
			.SelectMany((step, index) =>
				CreateMicroTasksForStep(step, index, step.CanRollback ? backupId : null, context));
		return backupId != null && steps.Any(x => x.CanRollback)
			?
			[
				_microTasksFactory.CreateDelegateMicroTask(
					(_, _) =>
					{
						_installBackupService.StartTracking(backupId);
						return ValueTask.CompletedTask;
					},
					MicroTaskDescription.PreparingInstallation.Instance),
				..microTasks,
			]
			: microTasks.ToArray();
	}

	public IReadOnlyList<IAppMicroTask> CreateRollbackMicroTasks(IReadOnlyList<InstallStep> steps,
		InstallBackupId backupId) =>
		steps
			.Select((step, stepIndex) => (Step: step, StepIndex: stepIndex))
			.Reverse()
			.Where(x => x.Step.CanRollback)
			.SelectMany(x => x.Step.Actions
				.Select((action, actionIndex) => (Action: action, Backup: backupId.ForAction(x.StepIndex, actionIndex)))
				.Reverse()
				.Select(y => y.Action switch
				{
					InstallAction.InstallMod(_, var modName, _) => CreateRollbackIfAppliedMicroTask(y.Backup,
						CreateRollbackInstallModMicroTask(modName)),
					InstallAction.SelectRequiredMod => null,
					InstallAction.ActivateMods(var activate, var mods) => CreateRollbackIfAppliedMicroTask(y.Backup,
						CreateRollbackActivateModsMicroTask(mods, !activate)),
					InstallAction.ActivatePlugins(var activate, var plugins) => CreateRollbackIfAppliedMicroTask(y.Backup,
						_microTasksFactory.CreateDelegateMicroTask(
							(_, ct) => _modOrganizerService.ActivatePlugins(plugins, !activate, ct).AsValueTask(),
							new MicroTaskDescription.ActivatePlugins(!activate))),
					InstallAction.RemoveFileSystemItem(var paths, _) => CreateRestoreBackupMicroTask(y.Backup,
						paths is [var path] ? new MicroTaskDescription.RestoreFile(path) : MicroTaskDescription.RestoreFiles.Instance),
					InstallAction.ExtractArchive(var downloadInfo, _) => CreateRestoreBackupMicroTask(y.Backup,
						new MicroTaskDescription.RevertArchiveExtraction(downloadInfo.FileName)),
					InstallAction.MergePlugins(var downloadInfo) => CreateRevertPluginsMergeMicroTask(y.Backup,
						downloadInfo.FileName),
					InstallAction.ChangeProfile => CreateRollbackIfAppliedMicroTask(y.Backup,
						_microTasksFactory.CreateDelegateMicroTask(
							(_, ct) => _modOrganizerService.ChangeProfile(_product.ModpackProfileName, ct).AsValueTask(),
							new MicroTaskDescription.ChangeProfile(_product.ModpackProfileName))),
				}))
			.OfType<IAppMicroTask>()
			.ToArray();

	/// <summary>
	/// Runs <paramref name="rollbackMicroTask"/>, which undoes an action that leaves no backup, only when the action ran
	/// (<see cref="IInstallBackupService.IsApplied"/>), then forgets that it ran, so a repeated rollback skips it. When
	/// the actions of its ID are not tracked (installed by a launcher that did not track them), whether it ran is
	/// unknown and it is undone anyway.
	/// </summary>
	private DelegateMicroTask CreateRollbackIfAppliedMicroTask(InstallActionBackup backup, IAppMicroTask rollbackMicroTask) =>
		new(rollbackMicroTask.Description, async (stepSubject, ct) =>
		{
			if (_installBackupService.IsTracked(backup.Id) && !_installBackupService.IsApplied(backup))
			{
				_logger.LogInformation("Action {Backup} did not run, skipping its rollback", backup.RelativePath);
				return;
			}

			await rollbackMicroTask.Execute(stepSubject, ct);
			_installBackupService.Delete(backup);
		});

	/// <summary>
	/// Restores the files the action deleted or overwrote and deletes the files it created. An action without a backup
	/// (its step's condition skipped it, or it was installed before the launcher took backups) is skipped.
	/// </summary>
	private IAppMicroTask CreateRestoreBackupMicroTask(InstallActionBackup backup, MicroTaskDescription description) =>
		_microTasksFactory.CreateDelegateMicroTask(
			(_, ct) => Task.Run(async () =>
			{
				if (!await _installBackupService.Restore(backup, ct))
				{
					_logger.LogWarning("Backup {Backup} does not exist, skipping its restoration", backup.RelativePath);
				}
			}, ct).AsValueTask(),
			description);

	/// <summary>
	/// Reverts the plugins merge, keeping what changed in the profile's plugin list since. A merge without a backup is
	/// skipped, like the actions of <see cref="CreateRestoreBackupMicroTask"/>.
	/// </summary>
	private IAppMicroTask CreateRevertPluginsMergeMicroTask(InstallActionBackup backup, string pluginsFileName) =>
		_microTasksFactory.CreateDelegateMicroTask(
			async (_, ct) =>
			{
				if (!await _modOrganizerService.RevertPluginsMerge(backup, ct))
				{
					_logger.LogWarning("Backup {Backup} does not exist, skipping reverting the plugins merge",
						backup.RelativePath);
				}
			},
			new MicroTaskDescription.RevertPluginsMerge(pluginsFileName));

	/// <summary>
	/// Deletes <paramref name="paths"/>, moving them into <paramref name="backup"/> when the step can be rolled back.
	/// </summary>
	private IAppMicroTask CreateRemoveFileSystemItemMicroTask(IReadOnlyList<string> paths, bool failOnFailedDelete,
		InstallActionBackup? backup)
	{
		MicroTaskDescription description = paths is [var path] ? new MicroTaskDescription.DeleteFile(path) : MicroTaskDescription.DeleteFiles.Instance;
		return backup == null
			? _microTasksFactory.CreateDeleteFileSystemItemsMicroTask(
				paths.Select(x => new SkyrimRelativePath(x)).ToArray(), failOnFailedDelete, description)
			: _microTasksFactory.CreateDelegateMicroTask(
				(_, ct) => Task.Run(() =>
				{
					foreach (var itemPath in paths)
					{
						if (_installBackupService.DeleteWithBackup(backup, new SkyrimRelativePath(itemPath), ct) is
						    FileSystemUtils.SafeDeleteResult.Error(var exception) && failOnFailedDelete)
						{
							throw new LauncherKit.Core.Exceptions.FileSystemItemDeletionException(itemPath, exception);
						}
					}
				}, ct).AsValueTask(),
				description);
	}

	/// <summary>
	/// Downloads and extracts the archive, first backing up the files it overwrites into <paramref name="backup"/> when
	/// the step can be rolled back.
	/// </summary>
	private IReadOnlyList<IAppMicroTask> CreateExtractArchiveMicroTasks(DownloadInfo downloadInfo,
		SkyrimRelativePath destinationPath, InstallActionBackup? backup)
	{
		var archiveName = downloadInfo.FileName;
		var download = _microTasksFactory.CreateDownloadFileMicroTask(downloadInfo);
		var extract = _microTasksFactory.CreateExtractSevenZipMicroTask(archiveName, destinationPath);
		return backup == null
			? [download, extract]
			:
			[
				download,
				_microTasksFactory.CreateDelegateMicroTask(
					(_, ct) => _installBackupService.BackupBeforeExtraction(backup,
						_fileStorageService.GetFilePath(archiveName)!, destinationPath, ct).AsValueTask(),
					new MicroTaskDescription.BackupArchiveFiles(archiveName)),
				extract,
			];
	}

	private IAppMicroTask CreateRollbackInstallModMicroTask(string modName) =>
		_microTasksFactory.CreateDelegateMicroTask(
			async (_, ct) =>
			{
				if (!_modOrganizerService.IsModInstalled(modName))
				{
					_logger.LogWarning("Mod {ModName} is not installed, skipping its uninstallation", modName);
					return;
				}

				await _modOrganizerService.UninstallMod(modName, ct);
			},
			new MicroTaskDescription.UninstallMod(modName));

	/// <summary>
	/// Restores the state of mods the steps activated or deactivated. A mod that is no longer installed has no state to
	/// restore, so it is skipped rather than failing the rollback.
	/// </summary>
	private IAppMicroTask CreateRollbackActivateModsMicroTask(IReadOnlyList<string> mods, bool activate) =>
		_microTasksFactory.CreateDelegateMicroTask(
			async (_, ct) =>
			{
				foreach (var mod in mods)
				{
					if (!_modOrganizerService.IsModInstalled(mod))
					{
						_logger.LogWarning("Mod {ModName} is not installed, skipping restoring its state", mod);
						continue;
					}

					await _modOrganizerService.ActivateMod(mod, activate, true, ct);
				}
			},
			new MicroTaskDescription.ActivateMods(activate));

	/// <param name="backupId">
	/// Where the step's actions back up what they delete or overwrite, or <see langword="null"/> when the step is never
	/// rolled back.
	/// </param>
	private IEnumerable<IAppMicroTask> CreateMicroTasksForStep(InstallStep step, int index, InstallBackupId? backupId,
		InstallContext context)
	{
		var stepMicroTasks = step.Actions.SelectMany((action, actionIndex) =>
			CreateMicroTasksForAction(action, index, backupId?.ForAction(index, actionIndex), context));

		return step.Condition != null
			? stepMicroTasks.Select(microTask => CreateConditionalMicroTask(microTask, step.Condition, context))
			: stepMicroTasks;
	}

	private IReadOnlyList<IAppMicroTask> CreateMicroTasksForAction(InstallAction action, int stepIndex,
		InstallActionBackup? backup, InstallContext context) =>
		action switch
		{
			InstallAction.InstallMod(var downloadInfo, var modName, var placement) =>
			[
				_microTasksFactory.CreateDownloadFileMicroTask(downloadInfo),
				MarkingApplied(backup,
					_microTasksFactory.CreateInstallModMicroTask(modName, downloadInfo.FileName, placement)),
			],
			InstallAction.SelectRequiredMod(var modNames) =>
				[_microTasksFactory.CreateSelectRequiredModMicroTask(context.GetContextForStep(stepIndex), modNames)],
			InstallAction.ActivateMods(var activate, var mods) =>
			[
				MarkingApplied(backup, _microTasksFactory.CreateDelegateMicroTask(
					async (_, ct) =>
					{
						foreach (var mod in mods)
						{
							await _modOrganizerService.ActivateMod(mod, activate, true, ct);
						}
					},
					new MicroTaskDescription.ActivateMods(activate))),
			],
			InstallAction.ActivatePlugins(var activate, var plugins) =>
			[
				MarkingApplied(backup, _microTasksFactory.CreateDelegateMicroTask(
					(_, ct) => _modOrganizerService.ActivatePlugins(plugins, activate, ct).AsValueTask(),
					new MicroTaskDescription.ActivatePlugins(activate))),
			],
			InstallAction.RemoveFileSystemItem(var paths, var failOnFailedDelete) =>
				[CreateRemoveFileSystemItemMicroTask(paths, failOnFailedDelete, backup)],
			InstallAction.ExtractArchive(var downloadInfo, var destinationPath) =>
				CreateExtractArchiveMicroTasks(downloadInfo, new SkyrimRelativePath(destinationPath), backup),
			InstallAction.MergePlugins(var downloadInfo) =>
			[
				_microTasksFactory.CreateDownloadFileMicroTask(downloadInfo),
				_microTasksFactory.CreateMergePluginsMicroTask(downloadInfo.FileName, backup),
			],
			InstallAction.ChangeProfile(var profileName) =>
			[
				MarkingApplied(backup, _microTasksFactory.CreateDelegateMicroTask(
					(_, ct) => _modOrganizerService.ChangeProfile(profileName, ct).AsValueTask(),
					new MicroTaskDescription.ChangeProfile(profileName))),
			],
		};

	/// <summary>
	/// <paramref name="microTask"/>, recording right before it runs that its action ran when the step can be rolled back
	/// (<paramref name="backup"/> is set), so an action interrupted halfway is undone too. Runs inside the step's
	/// condition like the action itself. Only for actions that leave no backup; the backup of the others tells that
	/// they ran.
	/// </summary>
	private IAppMicroTask MarkingApplied(InstallActionBackup? backup, IAppMicroTask microTask) =>
		backup == null
			? microTask
			: new DelegateMicroTask(microTask.Description, (stepSubject, ct) =>
			{
				_installBackupService.MarkApplied(backup);
				return microTask.Execute(stepSubject, ct);
			});

	private static IAppMicroTask CreateConditionalMicroTask(
		IAppMicroTask originalMicroTask, InstallCondition condition, InstallContext context) =>
		new DelegateMicroTask(originalMicroTask.Description, (stepSubject, ct) =>
		{
			var stepContext = context.GetContextForStep(condition.StepIndex);
			return stepContext.GetValue(condition.Key) is { } value && MatchCondition(value, condition.Operator, condition.Value)
				? originalMicroTask.Execute(stepSubject, ct)
				: ValueTask.CompletedTask;
		});

	private static bool MatchCondition(string value, CompareOperator compareOperator, string comparand) =>
		compareOperator switch
		{
			CompareOperator.Equal => value == comparand,
			CompareOperator.NotEqual => value != comparand,
			_ => throw new ArgumentOutOfRangeException(nameof(compareOperator), compareOperator, "Unknown compare operator"),
		};
}
