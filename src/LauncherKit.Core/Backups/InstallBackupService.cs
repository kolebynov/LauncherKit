using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LauncherKit.Core.Db;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.Backups;

/// <summary>
/// Stores the backup of an action in its own directory: <c>Files</c> holds the backed up files under their paths
/// relative to the Skyrim directory, and <c>Created.txt</c> lists the files and directories (ending with a directory
/// separator) an extraction created, one Skyrim-relative path per line. A plugins merge keeps the profile's
/// <c>plugins.txt</c> in <c>Plugins\Previous.txt</c>, the merged one in <c>Plugins\Merged.txt</c>, and the profile
/// name in <c>Plugins\Profile.txt</c>, written last so it marks a complete backup. The directory of an
/// <see cref="InstallBackupId"/> holds the empty <c>Installing</c> file while its installation is unfinished. Which
/// actions ran is recorded in the local database (<c>LocalDatabaseFileName</c>), next to the backups directory, by <see cref="InstallBackupId.RelativePath"/>
/// and action indexes.
/// </summary>
internal sealed class InstallBackupService : IInstallBackupService
{
	private const string FilesDirectoryName = "Files";
	private const string CreatedListFileName = "Created.txt";
	private const string PluginsDirectoryName = "Plugins";
	private const string PluginsProfileFileName = "Profile.txt";
	private const string PreviousPluginsFileName = "Previous.txt";
	private const string MergedPluginsFileName = "Merged.txt";
	private const string InstallationMarkFileName = "Installing";

	private const string StartTrackingSql =
		$"""INSERT OR IGNORE INTO "{DbConstants.TrackedInstallationsTableName}" ("BackupId") VALUES ($id)""";

	private const string IsTrackedSql =
		$"""SELECT 1 FROM "{DbConstants.TrackedInstallationsTableName}" WHERE "BackupId" = $id LIMIT 1""";

	private const string MarkAppliedSql =
		$"""
		 INSERT OR IGNORE INTO "{DbConstants.AppliedInstallActionsTableName}" ("BackupId", "StepIndex", "ActionIndex")
		 VALUES ($id, $step, $action)
		 """;

	private const string IsAppliedSql =
		$"""
		 SELECT 1 FROM "{DbConstants.AppliedInstallActionsTableName}"
		 WHERE "BackupId" = $id AND "StepIndex" = $step AND "ActionIndex" = $action LIMIT 1
		 """;

	private const string DeleteAppliedSql =
		$"""
		 DELETE FROM "{DbConstants.AppliedInstallActionsTableName}"
		 WHERE "BackupId" = $id AND "StepIndex" = $step AND "ActionIndex" = $action
		 """;

	// An ID and the IDs it contains, whose paths start with its path and a separator. substr rather than LIKE, whose
	// wildcards may occur in mod names.
	private const string DeleteIdRecordsSql =
		$"""
		 DELETE FROM "{DbConstants.AppliedInstallActionsTableName}"
		 WHERE "BackupId" = $id OR substr("BackupId", 1, length($prefix)) = $prefix;
		 DELETE FROM "{DbConstants.TrackedInstallationsTableName}"
		 WHERE "BackupId" = $id OR substr("BackupId", 1, length($prefix)) = $prefix;
		 """;

	private const string DeleteAllRecordsSql =
		$"""
		 DELETE FROM "{DbConstants.AppliedInstallActionsTableName}";
		 DELETE FROM "{DbConstants.TrackedInstallationsTableName}";
		 """;

	private readonly IOptionsSnapshot<AppSettings> _appSettings;
	private readonly LauncherProductOptions _product;
	private readonly ISevenZipService _sevenZipService;
	private readonly IFileSystemUtils _fileSystemUtils;
	private readonly SkyrimLocalDbContext _dbContext;
	private readonly ILogger<InstallBackupService> _logger;

	public InstallBackupService(IOptionsSnapshot<AppSettings> appSettings, ISevenZipService sevenZipService,
		IFileSystemUtils fileSystemUtils, SkyrimLocalDbContext dbContext, ILogger<InstallBackupService> logger,
		IOptions<LauncherProductOptions> product)
	{
		_appSettings = appSettings;
		_product = product.Value;
		_sevenZipService = sevenZipService;
		_fileSystemUtils = fileSystemUtils;
		_dbContext = dbContext;
		_logger = logger;
	}

	public FileSystemUtils.SafeDeleteResult DeleteWithBackup(InstallActionBackup backup, SkyrimRelativePath path,
		CancellationToken cancellationToken)
	{
		var skyrimDirectory = GetSkyrimDirectory();
		var itemPath = Path.TrimEndingDirectorySeparator(path.ToAbsolute(skyrimDirectory));
		var backupsDirectory = GetBackupsDirectory();
		if (string.Equals(itemPath, skyrimDirectory, StringComparison.OrdinalIgnoreCase) ||
		    IsSameOrInside(itemPath, backupsDirectory) || IsSameOrInside(backupsDirectory, itemPath))
		{
			throw new ArgumentException($"Path {path.Path} cannot be backed up", nameof(path));
		}

		var backupPath = Path.Combine(GetFilesDirectory(backup), Path.GetRelativePath(skyrimDirectory, itemPath));
		try
		{
			if (_fileSystemUtils.FileExists(itemPath))
			{
				_logger.LogInformation("Moving file {Path} to backup {Backup}", itemPath, backup.RelativePath);
				MoveToBackup(itemPath, backupPath);
				return FileSystemUtils.SafeDeleteResult.Success.Instance;
			}

			if (!_fileSystemUtils.DirectoryExists(itemPath))
			{
				return FileSystemUtils.SafeDeleteResult.NotExist.Instance;
			}

			_logger.LogInformation("Moving directory {Path} to backup {Backup}", itemPath, backup.RelativePath);
			MoveDirectoryToBackup(itemPath, backupPath, cancellationToken);
			return FileSystemUtils.SafeDeleteResult.Success.Instance;
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			_logger.LogWarning(e, "Failed to move {Path} to backup {Backup}", itemPath, backup.RelativePath);
			return new FileSystemUtils.SafeDeleteResult.Error(e);
		}
	}

	public async Task BackupBeforeExtraction(InstallActionBackup backup, string archivePath,
		SkyrimRelativePath destinationPath, CancellationToken cancellationToken)
	{
		var skyrimDirectory = GetSkyrimDirectory();
		var destinationDirectory = destinationPath.ToAbsolute(skyrimDirectory);
		var filesDirectory = GetFilesDirectory(backup);
		var createdListPath = Path.Combine(GetActionDirectory(backup), CreatedListFileName);
		var createdList = _fileSystemUtils.FileExists(createdListPath)
			? (await _fileSystemUtils.ReadAllLines(createdListPath, cancellationToken)).ToList()
			: [];
		var created = createdList.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var createdCount = created.Count;
		var backedUpCount = 0;

		await foreach (var entry in _sevenZipService.ListArchiveEntries(archivePath, cancellationToken))
		{
			var targetPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(entry.Path, destinationDirectory));
			var relativePath = GetSkyrimRelativePath(skyrimDirectory, targetPath);
			AddCreatedDirectories(entry.IsDirectory ? targetPath : Path.GetDirectoryName(targetPath)!);
			if (entry.IsDirectory || created.Contains(relativePath))
			{
				continue;
			}

			var backupPath = Path.Combine(filesDirectory, relativePath);
			if (_fileSystemUtils.FileExists(backupPath))
			{
				// Backed up by an earlier run of this action, which extracted the file now in its place.
				continue;
			}

			if (_fileSystemUtils.FileExists(targetPath))
			{
				MoveToBackup(targetPath, backupPath);
				backedUpCount++;
			}
			else
			{
				AddCreated(relativePath);
			}
		}

		// Written only now, before anything is extracted: a file missing from the list was not extracted by this action.
		if (createdList.Count > createdCount)
		{
			await WriteAllLines(createdListPath, createdList, cancellationToken);
		}

		_logger.LogInformation(
			"Backed up {BackedUpCount} files before extracting {ArchivePath} to {Destination}, {CreatedCount} new items recorded in {Backup}",
			backedUpCount, archivePath, destinationDirectory, createdList.Count - createdCount, backup.RelativePath);

		void AddCreatedDirectories(string directory)
		{
			while (!string.Equals(directory, skyrimDirectory, StringComparison.OrdinalIgnoreCase) &&
			       !_fileSystemUtils.DirectoryExists(directory))
			{
				AddCreated(GetSkyrimRelativePath(skyrimDirectory, directory) + Path.DirectorySeparatorChar);
				directory = Path.GetDirectoryName(directory)!;
			}
		}

		void AddCreated(string relativePath)
		{
			if (created.Add(relativePath))
			{
				createdList.Add(relativePath);
			}
		}
	}

	public async Task<bool> Restore(InstallActionBackup backup, CancellationToken cancellationToken)
	{
		var actionDirectory = GetActionDirectory(backup);
		if (!_fileSystemUtils.DirectoryExists(actionDirectory))
		{
			return false;
		}

		_logger.LogInformation("Restoring backup {Backup}", backup.RelativePath);
		var skyrimDirectory = GetSkyrimDirectory();
		var createdListPath = Path.Combine(actionDirectory, CreatedListFileName);
		var createdDirectories = new List<string>();
		var createdList = _fileSystemUtils.FileExists(createdListPath)
			? await _fileSystemUtils.ReadAllLines(createdListPath, cancellationToken)
			: [];
		foreach (var relativePath in createdList)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var path = Path.Combine(skyrimDirectory, relativePath);
			if (Path.EndsInDirectorySeparator(relativePath))
			{
				createdDirectories.Add(Path.TrimEndingDirectorySeparator(path));
			}
			else
			{
				DeleteFile(path);
			}
		}

		var filesDirectory = GetFilesDirectory(backup);
		if (_fileSystemUtils.DirectoryExists(filesDirectory))
		{
			foreach (var directory in _fileSystemUtils.GetDirectoriesRecursive(filesDirectory))
			{
				_fileSystemUtils.EnsureDirectoryExists(
					Path.Combine(skyrimDirectory, Path.GetRelativePath(filesDirectory, directory)), false);
			}

			foreach (var file in _fileSystemUtils.GetFilesRecursive(filesDirectory))
			{
				cancellationToken.ThrowIfCancellationRequested();
				var targetPath = Path.Combine(skyrimDirectory, Path.GetRelativePath(filesDirectory, file));
				_fileSystemUtils.EnsureDirectoryExists(Path.GetDirectoryName(targetPath)!, false);
				_fileSystemUtils.MoveFile(file, targetPath, overwrite: true);
			}
		}

		// Deepest first, so a directory whose created subdirectories were deleted is empty in turn.
		foreach (var directory in createdDirectories.OrderByDescending(x => x.Length))
		{
			if (_fileSystemUtils.DirectoryExists(directory) && _fileSystemUtils.IsDirectoryEmpty(directory))
			{
				DeleteDirectory(directory);
			}
		}

		DeleteDirectory(actionDirectory);
		return true;
	}

	public async Task BackupPluginsBeforeMerge(InstallActionBackup backup, PluginsMergeBackup pluginsBackup,
		CancellationToken cancellationToken)
	{
		var directory = Path.Combine(GetActionDirectory(backup), PluginsDirectoryName);
		var profilePath = Path.Combine(directory, PluginsProfileFileName);
		if (_fileSystemUtils.FileExists(profilePath))
		{
			// Taken by an earlier run of this action, before the plugins now in the profile were merged.
			_logger.LogInformation("Plugins are already backed up in {Backup}", backup.RelativePath);
			return;
		}

		_logger.LogInformation("Backing up plugins of profile {Profile} in {Backup}", pluginsBackup.Profile,
			backup.RelativePath);
		await WriteAllLines(Path.Combine(directory, PreviousPluginsFileName), pluginsBackup.PreviousLines,
			cancellationToken);
		await WriteAllLines(Path.Combine(directory, MergedPluginsFileName), pluginsBackup.MergedLines, cancellationToken);
		await WriteAllLines(profilePath, [pluginsBackup.Profile], cancellationToken);
	}

	public async Task<PluginsMergeBackup?> ReadPluginsMergeBackup(InstallActionBackup backup,
		CancellationToken cancellationToken)
	{
		var directory = Path.Combine(GetActionDirectory(backup), PluginsDirectoryName);
		var profilePath = Path.Combine(directory, PluginsProfileFileName);
		if (!_fileSystemUtils.FileExists(profilePath))
		{
			return null;
		}

		var profile = await _fileSystemUtils.ReadAllLines(profilePath, cancellationToken) is [var name]
			? name
			: throw new InvalidOperationException($"Backup {backup.RelativePath} has an invalid profile file");
		return new PluginsMergeBackup(profile,
			await _fileSystemUtils.ReadAllLines(Path.Combine(directory, PreviousPluginsFileName), cancellationToken),
			await _fileSystemUtils.ReadAllLines(Path.Combine(directory, MergedPluginsFileName), cancellationToken));
	}

	public async Task StartInstallation(InstallBackupId id, CancellationToken cancellationToken)
	{
		_logger.LogInformation("Starting installation {Backup}", id.RelativePath);
		// Before the mark, so an unfinished installation is always tracked and rolling it back skips what did not run.
		StartTracking(id);
		var markPath = GetInstallationMarkPath(id);
		_fileSystemUtils.EnsureDirectoryExists(Path.GetDirectoryName(markPath)!, false);
		await _fileSystemUtils.WriteAllText(markPath, string.Empty, cancellationToken);
	}

	public bool IsInstallationUnfinished(InstallBackupId id) => _fileSystemUtils.FileExists(GetInstallationMarkPath(id));

	public void FinishInstallation(InstallBackupId id)
	{
		_logger.LogInformation("Finishing installation {Backup}", id.RelativePath);
		var markPath = GetInstallationMarkPath(id);
		DeleteFile(markPath);
		DeleteEmptyDirectories(Path.GetDirectoryName(markPath)!);
	}

	public void StartTracking(InstallBackupId id) => _dbContext.Execute(StartTrackingSql, ("$id", id.RelativePath));

	public bool IsTracked(InstallBackupId id) =>
		_dbContext.Read(IsTrackedSql, _ => true, ("$id", id.RelativePath)).Count > 0;

	public void MarkApplied(InstallActionBackup backup) =>
		_dbContext.Execute(MarkAppliedSql, ("$id", backup.Id.RelativePath), ("$step", backup.StepIndex),
			("$action", backup.ActionIndex));

	public bool IsApplied(InstallActionBackup backup) =>
		_dbContext.Read(IsAppliedSql, _ => true, ("$id", backup.Id.RelativePath), ("$step", backup.StepIndex),
			("$action", backup.ActionIndex)).Count > 0;

	public void Delete(InstallActionBackup backup)
	{
		_logger.LogInformation("Deleting backup {Backup}", backup.RelativePath);
		_dbContext.Execute(DeleteAppliedSql, ("$id", backup.Id.RelativePath), ("$step", backup.StepIndex),
			("$action", backup.ActionIndex));
		DeleteDirectory(GetActionDirectory(backup));
	}

	public void Delete(InstallBackupId id)
	{
		_dbContext.Execute(DeleteIdRecordsSql, ("$id", id.RelativePath),
			("$prefix", id.RelativePath + Path.DirectorySeparatorChar));
		var directory = Path.Combine(GetBackupsDirectory(), id.RelativePath);
		if (!_fileSystemUtils.DirectoryExists(directory))
		{
			return;
		}

		_logger.LogInformation("Deleting backups {Backup}", id.RelativePath);
		DeleteDirectory(directory);
		DeleteEmptyDirectories(Path.GetDirectoryName(directory)!);
	}

	public void DeleteAll()
	{
		var backupsDirectory = GetBackupsDirectory();
		_logger.LogInformation("Deleting all backups in {Directory}", backupsDirectory);
		_dbContext.Execute(DeleteAllRecordsSql);
		DeleteDirectory(backupsDirectory);
	}

	private void MoveToBackup(string path, string backupPath)
	{
		if (_fileSystemUtils.FileExists(backupPath))
		{
			// Backed up by an earlier run of this action, so the file now in its place came later.
			DeleteFile(path);
			return;
		}

		_fileSystemUtils.EnsureDirectoryExists(Path.GetDirectoryName(backupPath)!, false);
		_fileSystemUtils.MoveFile(path, backupPath);
	}

	/// <summary>
	/// Deletes <paramref name="directory"/> when it is empty, then each parent left empty, up to the backups directory.
	/// </summary>
	private void DeleteEmptyDirectories(string directory)
	{
		var backupsDirectory = GetBackupsDirectory();
		for (; !string.Equals(directory, backupsDirectory, StringComparison.OrdinalIgnoreCase) &&
		       _fileSystemUtils.DirectoryExists(directory) && _fileSystemUtils.IsDirectoryEmpty(directory);
		     directory = Path.GetDirectoryName(directory)!)
		{
			DeleteDirectory(directory);
		}
	}

	/// <summary>Writes the file through a temporary file, so it is either complete or missing.</summary>
	private async Task WriteAllLines(string path, IEnumerable<string> lines, CancellationToken cancellationToken)
	{
		_fileSystemUtils.EnsureDirectoryExists(Path.GetDirectoryName(path)!, false);
		var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
		await _fileSystemUtils.WriteAllLines(temporaryPath, lines, cancellationToken);
		_fileSystemUtils.MoveFile(temporaryPath, path, overwrite: true);
	}

	/// <summary>
	/// Moves the files one by one, so a failure or an interruption leaves each file either in place or in the backup,
	/// and recreates the subdirectories in the backup, so the empty ones are restored too.
	/// </summary>
	private void MoveDirectoryToBackup(string directory, string backupDirectory, CancellationToken cancellationToken)
	{
		_fileSystemUtils.EnsureDirectoryExists(backupDirectory, false);
		foreach (var subdirectory in _fileSystemUtils.GetDirectoriesRecursive(directory))
		{
			_fileSystemUtils.EnsureDirectoryExists(
				Path.Combine(backupDirectory, Path.GetRelativePath(directory, subdirectory)), false);
		}

		foreach (var file in _fileSystemUtils.GetFilesRecursive(directory))
		{
			cancellationToken.ThrowIfCancellationRequested();
			MoveToBackup(file, Path.Combine(backupDirectory, Path.GetRelativePath(directory, file)));
		}

		DeleteDirectory(directory);
	}

	/// <summary>Deletes the file, if any, rethrowing the exception of a failed deletion.</summary>
	private void DeleteFile(string path) => ThrowIfFailed(_fileSystemUtils.SafeDeleteFile(path));

	/// <summary>Deletes the directory with its contents, if any, rethrowing the exception of a failed deletion.</summary>
	private void DeleteDirectory(string path) => ThrowIfFailed(_fileSystemUtils.SafeDeleteDirectory(path));

	private static void ThrowIfFailed(FileSystemUtils.SafeDeleteResult result)
	{
		if (result is FileSystemUtils.SafeDeleteResult.Error(var exception))
		{
			ExceptionDispatchInfo.Throw(exception);
		}
	}

	private static string GetSkyrimRelativePath(string skyrimDirectory, string path)
	{
		var relativePath = Path.GetRelativePath(skyrimDirectory, path);
		return relativePath == "." || Path.IsPathRooted(relativePath) || relativePath == ".." ||
		       relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
			? throw new InvalidOperationException($"Path {path} is outside the Skyrim directory {skyrimDirectory}")
			: relativePath;
	}

	private static bool IsSameOrInside(string directory, string path) =>
		string.Equals(directory, path, StringComparison.OrdinalIgnoreCase) ||
		path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

	private string GetSkyrimDirectory() =>
		Path.TrimEndingDirectorySeparator(Path.GetFullPath(_appSettings.Value.SkyrimDirectory ??
		                                                   throw new InvalidOperationException("Skyrim directory is not set")));

	private string GetBackupsDirectory() =>
		Path.Combine(Path.GetFullPath(_appSettings.Value.GetSkyrimAppDataDirectory(_product) ??
		                              throw new InvalidOperationException("Skyrim directory is not set")), "Backups");

	private string GetActionDirectory(InstallActionBackup backup) =>
		Path.Combine(GetBackupsDirectory(), backup.RelativePath);

	private string GetFilesDirectory(InstallActionBackup backup) =>
		Path.Combine(GetActionDirectory(backup), FilesDirectoryName);

	private string GetInstallationMarkPath(InstallBackupId id) =>
		Path.Combine(GetBackupsDirectory(), id.RelativePath, InstallationMarkFileName);
}
