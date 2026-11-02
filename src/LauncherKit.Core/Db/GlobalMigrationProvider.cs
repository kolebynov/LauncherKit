﻿namespace LauncherKit.Core.Db;

internal sealed class GlobalMigrationProvider : IMigrationProvider<GlobalDbContext>
{
	private static readonly DbMigration Initial = new()
	{
		Name = nameof(Initial),
		Script = $"""
		          CREATE TABLE IF NOT EXISTS "{DbConstants.FilesTableName}" (
		              "FileName" TEXT PRIMARY KEY,
		              "Path" TEXT NOT NULL,
		              "WrittenBytes" INTEGER NOT NULL,
		              "CreatedAt" INTEGER NOT NULL,
		              "FinishedAt" INTEGER NULL
		          );

		          CREATE TABLE IF NOT EXISTS "{DbConstants.KeyValuesTableName}" (
		              "Key" TEXT PRIMARY KEY,
		              "Value" TEXT NULL
		          );
		          """,
	};

	private static readonly DbMigration AddSizeColumn = new()
	{
		Name = nameof(AddSizeColumn),
		Script = $"""
		          ALTER TABLE "{DbConstants.FilesTableName}" ADD COLUMN "Size" INTEGER NOT NULL DEFAULT 0;
		          DELETE FROM "{DbConstants.FilesTableName}" WHERE "FinishedAt" IS NULL;
		          UPDATE "{DbConstants.FilesTableName}" SET "Size" = "WrittenBytes";
		          """,
	};

	private static readonly DbMigration AddTorrentsTable = new()
	{
		Name = nameof(AddTorrentsTable),
		Script = """
		         CREATE TABLE IF NOT EXISTS "Torrents" (
		             "InfoHashes" TEXT PRIMARY KEY,
		             "MagnetLink" TEXT NOT NULL,
		             "SaveDirectory" TEXT NOT NULL,
		             "IsCompleted" INTEGER NOT NULL
		         );
		         """,
	};

	private static readonly DbMigration DropTorrentsTable = new()
	{
		Name = nameof(DropTorrentsTable),
		Script = """
		         DROP TABLE IF EXISTS "Torrents";
		         """,
	};

	private static readonly DbMigration AddHashColumn = new()
	{
		Name = nameof(AddHashColumn),
		Script = $"""
		          ALTER TABLE "{DbConstants.FilesTableName}" ADD COLUMN "Hash" TEXT;
		          """,
	};

	private static readonly DbMigration AddFileParts = new()
	{
		Name = nameof(AddFileParts),
		Script = $"""
		          CREATE TABLE IF NOT EXISTS "{DbConstants.FilePartsTableName}" (
		              "FileName" TEXT,
		              "Index" INTEGER NOT NULL,
		              "WrittenBytes" INTEGER NOT NULL,
		              "Size" INTEGER NOT NULL
		          );

		          INSERT INTO "{DbConstants.FilePartsTableName}" ("FileName", "Index", "WrittenBytes", "Size")
		          SELECT "FileName", 0, "WrittenBytes", "Size" FROM "{DbConstants.FilesTableName}"
		          WHERE "FinishedAt" IS NULL;

		          ALTER TABLE "{DbConstants.FilesTableName}"
		          DROP COLUMN "WrittenBytes";
		          """
	};

	/// <summary>
	/// The real last write time of existing parts is unknown, so they get their file's creation time, the
	/// moment the cleanup measured their age from before.
	/// </summary>
	private static readonly DbMigration AddFilePartLastWriteAt = new()
	{
		Name = nameof(AddFilePartLastWriteAt),
		Script = $"""
		          ALTER TABLE "{DbConstants.FilePartsTableName}" ADD COLUMN "LastWriteAt" INTEGER NOT NULL DEFAULT 0;

		          UPDATE "{DbConstants.FilePartsTableName}"
		          SET "LastWriteAt" = COALESCE(
		              (SELECT "CreatedAt" FROM "{DbConstants.FilesTableName}"
		               WHERE "{DbConstants.FilesTableName}"."FileName" = "{DbConstants.FilePartsTableName}"."FileName"),
		              0);
		          """,
	};

	public IReadOnlyList<DbMigration> GetMigrations() =>
	[
		Initial, AddSizeColumn, AddTorrentsTable, DropTorrentsTable, AddHashColumn, AddFileParts,
		AddFilePartLastWriteAt,
	];
}