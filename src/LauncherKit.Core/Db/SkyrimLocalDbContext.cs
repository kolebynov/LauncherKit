namespace LauncherKit.Core.Db;

/// <summary>Installation-local data in <c>stb.db</c>.</summary>
internal sealed class SkyrimLocalDbContext : SqliteDbContext
{
	public SkyrimLocalDbContext(string dbPath)
		: base(dbPath)
	{
	}
}
