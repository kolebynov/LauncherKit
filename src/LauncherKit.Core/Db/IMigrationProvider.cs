﻿namespace LauncherKit.Core.Db;

internal interface IMigrationProvider<TContext>
	where TContext : SqliteDbContext
{
	IReadOnlyList<DbMigration> GetMigrations();
}