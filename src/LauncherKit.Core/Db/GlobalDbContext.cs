﻿namespace LauncherKit.Core.Db;

/// <summary>Launcher-wide data in <c>launcher.db</c>.</summary>
public sealed class GlobalDbContext : SqliteDbContext
{
	public GlobalDbContext(string dbPath)
		: base(dbPath)
	{
	}
}