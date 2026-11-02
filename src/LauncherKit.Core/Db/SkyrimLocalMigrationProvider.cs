namespace LauncherKit.Core.Db;

internal sealed class SkyrimLocalMigrationProvider : IMigrationProvider<SkyrimLocalDbContext>
{
	private static readonly DbMigration Initial = new()
	{
		Name = nameof(Initial),
		Script = $"""
		          CREATE TABLE IF NOT EXISTS "{DbConstants.FilesTableName}" (
		              "FileName" TEXT PRIMARY KEY,
		              "WrittenBytes" INTEGER NOT NULL,
		              "IsFinished" INTEGER NOT NULL DEFAULT 0
		          );

		          CREATE TABLE IF NOT EXISTS "{DbConstants.KeyValuesTableName}" (
		              "Key" TEXT PRIMARY KEY,
		              "Value" TEXT NULL
		          );
		          """,
	};

	private static readonly DbMigration RemoveFilesTable = new()
	{
		Name = nameof(RemoveFilesTable),
		Script = $"""DROP TABLE "{DbConstants.FilesTableName}";""",
	};

	/// <summary>
	/// Records which install actions ran (<see cref="Backups.InstallBackupService"/>), so a rollback undoes only those.
	/// </summary>
	private static readonly DbMigration AddInstallActionTracking = new()
	{
		Name = nameof(AddInstallActionTracking),
		Script = $"""
		          CREATE TABLE IF NOT EXISTS "{DbConstants.TrackedInstallationsTableName}" (
		              "BackupId" TEXT PRIMARY KEY
		          );

		          CREATE TABLE IF NOT EXISTS "{DbConstants.AppliedInstallActionsTableName}" (
		              "BackupId" TEXT NOT NULL,
		              "StepIndex" INTEGER NOT NULL,
		              "ActionIndex" INTEGER NOT NULL,
		              PRIMARY KEY ("BackupId", "StepIndex", "ActionIndex")
		          );
		          """,
	};

	private readonly DbMigration _addTimestampColumnsForFiles;

	public SkyrimLocalMigrationProvider(TimeProvider timeProvider)
	{
		var now = timeProvider.GetUtcNow().ToUnixTimeSeconds();
		_addTimestampColumnsForFiles = new DbMigration
		{
			Name = "AddTimestampColumnsForFiles",
			Script = $"""
			          ALTER TABLE "{DbConstants.FilesTableName}" ADD COLUMN "CreatedAt" INTEGER NOT NULL DEFAULT {now};
			          ALTER TABLE "{DbConstants.FilesTableName}" ADD COLUMN "FinishedAt" INTEGER NULL;
			          UPDATE "{DbConstants.FilesTableName}" SET "FinishedAt" = {now} WHERE "IsFinished" != 0;
			          """,
		};
	}

	public IReadOnlyList<DbMigration> GetMigrations() =>
		[Initial, _addTimestampColumnsForFiles, RemoveFilesTable, AddInstallActionTracking];
}
