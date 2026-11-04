using LauncherKit.Core.Models;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.Backups;

/// <summary>
/// Keeps the files that install actions delete or overwrite in the Skyrim directory, so rolling the actions back
/// restores them. Backups are kept in the product-specific installation data directory’s <c>Backups</c> subdirectory of the Skyrim directory, on the same volume, so taking
/// and restoring them moves files rather than copying them.
/// </summary>
/// <remarks>
/// Every method can be repeated after a failure or an interruption: a file that already has a backup keeps it, since the
/// file now in its place was put there by an earlier run of the same action.
/// </remarks>
public interface IInstallBackupService
{
	/// <summary>
	/// Moves the file or directory <paramref name="path"/> into <paramref name="backup"/> instead of deleting it. A file
	/// whose backup already exists is deleted. A failure stops at the file that failed, leaving the files moved before it
	/// in the backup.
	/// </summary>
	FileSystemUtils.SafeDeleteResult DeleteWithBackup(InstallActionBackup backup, SkyrimRelativePath path,
		CancellationToken cancellationToken);

	/// <summary>
	/// Prepares extracting the archive <paramref name="archivePath"/> into <paramref name="destinationPath"/>: moves
	/// the files it will overwrite into <paramref name="backup"/> and records the files and directories it will create.
	/// </summary>
	Task BackupBeforeExtraction(InstallActionBackup backup, string archivePath, SkyrimRelativePath destinationPath,
		CancellationToken cancellationToken);

	/// <summary>
	/// Undoes the action <paramref name="backup"/> was taken for: deletes the files it recorded as created, moves the
	/// backed up files back (replacing the files now in their place), deletes the created directories left empty, and
	/// then deletes the backup.
	/// </summary>
	/// <returns><see langword="false"/> when there is no such backup.</returns>
	Task<bool> Restore(InstallActionBackup backup, CancellationToken cancellationToken);

	/// <summary>
	/// Keeps <paramref name="pluginsBackup"/> in <paramref name="backup"/> before the plugins are merged, unless the
	/// backup already holds one.
	/// </summary>
	Task BackupPluginsBeforeMerge(InstallActionBackup backup, PluginsMergeBackup pluginsBackup,
		CancellationToken cancellationToken);

	/// <returns>
	/// The plugins kept by <see cref="BackupPluginsBeforeMerge"/>, or <see langword="null"/> when there are none.
	/// </returns>
	Task<PluginsMergeBackup?> ReadPluginsMergeBackup(InstallActionBackup backup, CancellationToken cancellationToken);

	/// <summary>
	/// Marks the installation of the install steps of <paramref name="id"/> as started, until
	/// <see cref="FinishInstallation"/>, and starts tracking its actions (<see cref="StartTracking"/>). Deleting the
	/// backups of <paramref name="id"/> deletes the mark too.
	/// </summary>
	Task StartInstallation(InstallBackupId id, CancellationToken cancellationToken);

	/// <summary>Whether the installation of <paramref name="id"/> was started and not finished.</summary>
	bool IsInstallationUnfinished(InstallBackupId id);

	/// <summary>Removes the mark of <see cref="StartInstallation"/>, keeping the backups.</summary>
	void FinishInstallation(InstallBackupId id);

	/// <summary>
	/// Records that the actions of <paramref name="id"/> are tracked: from now on an action of it ran only when
	/// <see cref="MarkApplied"/> recorded it. Repeating it has no effect.
	/// </summary>
	void StartTracking(InstallBackupId id);

	/// <summary>
	/// Whether the actions of <paramref name="id"/> are tracked. They are not when the steps were installed by a
	/// launcher that did not track them, so whether an action ran is unknown.
	/// </summary>
	bool IsTracked(InstallBackupId id);

	/// <summary>Records that the action <paramref name="backup"/> was taken for ran. Repeating it has no effect.</summary>
	void MarkApplied(InstallActionBackup backup);

	/// <summary>Whether <see cref="MarkApplied"/> recorded the action and it was not deleted since.</summary>
	bool IsApplied(InstallActionBackup backup);

	/// <summary>Deletes the backup of one action and its <see cref="MarkApplied"/> record without restoring it.</summary>
	void Delete(InstallActionBackup backup);

	/// <summary>
	/// Deletes the backups and records of <paramref name="id"/>, including the IDs it contains, without restoring them.
	/// </summary>
	void Delete(InstallBackupId id);

	/// <summary>
	/// Deletes every backup and record without restoring it, when the Mod Organizer directory with the installed mods
	/// is deleted (the modpack uninstalled or reinstalled): no installed mod is left to roll back, and restoring the backups of
	/// a mod installed again later could bring back outdated files.
	/// </summary>
	void DeleteAll();
}
