using Microsoft.Extensions.Logging.Abstractions;
using LauncherKit.Core.Db;
using LauncherKit.TestSupport;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.Tests.TestSupport;

/// <summary>
/// Temp-file SQLite databases opened through the production contexts. A file database (not <c>:memory:</c>) is
/// required because every context opens its own connection. The contexts do not pool connections, so the
/// temporary directory can be deleted once they are disposed.
/// </summary>
internal sealed class SqliteDatabase : IDisposable
{
	private readonly TempDirectory _directory = new();

	public SqliteDatabase()
	{
		var product = ProductTestSupport.CreateProduct();
		GlobalDbPath = _directory.Combine(LauncherConstants.GlobalDatabaseFileName);
		SkyrimLocalDbPath = _directory.Combine(product.LocalDatabaseFileName);
	}

	public string GlobalDbPath { get; }

	public string SkyrimLocalDbPath { get; }

	public GlobalDbContext CreateGlobalContext() => new(GlobalDbPath);

	public SkyrimLocalDbContext CreateSkyrimLocalContext() => new(SkyrimLocalDbPath);

	/// <summary>
	/// Bootstraps the schema with the shipped migrations, so fixtures cannot drift from production DDL.
	/// </summary>
	public GlobalDbContext CreateMigratedGlobalContext()
	{
		var context = CreateGlobalContext();
		Migrate(context, new GlobalMigrationProvider());
		return context;
	}

	public SkyrimLocalDbContext CreateMigratedSkyrimLocalContext()
	{
		var context = CreateSkyrimLocalContext();
		Migrate(context, new SkyrimLocalMigrationProvider(TimeProvider.System));
		return context;
	}

	public void Dispose() => _directory.Dispose();

	private static void Migrate<TContext>(TContext context, IMigrationProvider<TContext> migrationProvider)
		where TContext : SqliteDbContext =>
		new DbMigrator<TContext>(context, migrationProvider, NullLogger<DbMigrator<TContext>>.Instance, TimeProvider.System)
			.Migrate();
}
