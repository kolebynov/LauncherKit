namespace LauncherKit.Core.Db;

internal interface IDbMigrator<TContext>
	where TContext : SqliteDbContext
{
	void Migrate();
}
