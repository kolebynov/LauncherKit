namespace LauncherKit.Core.Db;

/// <summary>Installation-local data in the product's <c>LocalDatabaseFileName</c>.</summary>
internal sealed class SkyrimLocalDbContext : SqliteDbContext
{
	public SkyrimLocalDbContext(string dbPath)
		: base(dbPath)
	{
	}
}
