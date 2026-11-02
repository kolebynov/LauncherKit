using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.Core.Backups;
using LauncherKit.Core.Feed.Install;

namespace LauncherKit.Core.AppTasks.InstallSteps;

/// <summary>
/// Creates microtasks from declarative feed <see cref="InstallStep"/>s and their <see cref="InstallAction"/>s, for any
/// task that installs from the feed. Resolve it from <see cref="MicroTasksProviderContext.ServiceProvider"/>, so its
/// microtasks come from the scope the task runs in.
/// </summary>
public interface IInstallStepsMicroTasksFactory
{
	/// <summary>
	/// Microtasks running <paramref name="steps"/> in order. A step's <see cref="InstallStep.Condition"/> refers to a step
	/// of the same list by its index, so steps passed to separate calls do not see each other's results.
	/// </summary>
	/// <param name="steps">The steps to run.</param>
	/// <param name="backupId">
	/// Where the <see cref="InstallStep.CanRollback"/> steps back up the files their <c>removeFileSystemItem</c> actions
	/// delete and their <c>extractArchive</c> actions overwrite or create, and record which of their other actions ran,
	/// for <see cref="CreateRollbackMicroTasks"/> with the same ID; <see langword="null"/> for steps that are never
	/// rolled back, which then delete and overwrite without a backup.
	/// </param>
	IReadOnlyList<IAppMicroTask> CreateInstallMicroTasks(IReadOnlyList<InstallStep> steps, InstallBackupId? backupId);

	/// <summary>
	/// Microtasks undoing the <see cref="InstallStep.CanRollback"/> steps of <paramref name="steps"/> in reverse order,
	/// the actions of a step reversed too: a mod an action installed is uninstalled, mods and plugins get the opposite
	/// state, a profile change returns to the STB profile, and deleted or overwritten files are restored from the backups
	/// of <paramref name="backupId"/> (files an extraction created are deleted); other actions are not undone. Only the
	/// actions that ran are undone: an action without a backup, or one not recorded as run, is skipped (its step's
	/// condition skipped it, or the installation stopped before it). Actions of steps installed by a launcher that did
	/// not record them are undone whenever they have no backup to tell otherwise, and a mod that is not installed is
	/// skipped.
	/// </summary>
	IReadOnlyList<IAppMicroTask> CreateRollbackMicroTasks(IReadOnlyList<InstallStep> steps, InstallBackupId backupId);
}
