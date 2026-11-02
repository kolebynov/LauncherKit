﻿namespace LauncherKit.Core.Db;

internal sealed class DbMigration
{
	public required string Name { get; init; }

	public required string Script { get; init; }
}