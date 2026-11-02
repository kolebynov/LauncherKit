﻿using Microsoft.Extensions.Logging;

namespace LauncherKit.Core.Db;

internal sealed class DbMigrator<TContext> : IDbMigrator<TContext>
	where TContext : SqliteDbContext
{
	private const string CreateMigrationTableSql =
		$"""
		 CREATE TABLE IF NOT EXISTS "{DbConstants.MigrationsTableName}" (
		     "Name" TEXT PRIMARY KEY,
		     "AppliedAt" INTEGER NOT NULL,
		     "Index" INTEGER NOT NULL
		 );
		 """;

	private const string SelectLastAppliedIndexSql =
		$"""SELECT "Index" FROM "{DbConstants.MigrationsTableName}" ORDER BY "Index" DESC LIMIT 1""";

	private const string InsertAppliedMigrationSql =
		$"""
		 INSERT INTO "{DbConstants.MigrationsTableName}" ("Name", "AppliedAt", "Index")
		 VALUES ($name, $appliedAt, $index)
		 """;

	private readonly TContext _dbContext;
	private readonly IMigrationProvider<TContext> _migrationProvider;
	private readonly ILogger<DbMigrator<TContext>> _logger;
	private readonly TimeProvider _timeProvider;

	public DbMigrator(TContext dbContext, IMigrationProvider<TContext> migrationProvider,
		ILogger<DbMigrator<TContext>> logger, TimeProvider timeProvider)
	{
		_dbContext = dbContext;
		_migrationProvider = migrationProvider;
		_logger = logger;
		_timeProvider = timeProvider;
	}

	public void Migrate()
	{
		_logger.LogInformation("Migrating database {DbContext}", typeof(TContext).Name);

		_dbContext.Execute(CreateMigrationTableSql);
		var lastAppliedMigrationIndex = _dbContext.Read(SelectLastAppliedIndexSql, x => x.GetInt32(0))
			.DefaultIfEmpty(-1)
			.First();
		var migrations = _migrationProvider.GetMigrations();
		if (lastAppliedMigrationIndex >= migrations.Count - 1)
		{
			_logger.LogInformation("Database is up to date");
			return;
		}

		// Microsoft.Data.Sqlite requires every command run inside a transaction to reference it.
		using var transaction = _dbContext.BeginTransaction();
		foreach (var (index, migration) in migrations.Index().Skip(lastAppliedMigrationIndex + 1))
		{
			_logger.LogInformation("Applying migration {Index}: {Name}", index, migration.Name);
			using (var command = _dbContext.CreateCommand(migration.Script))
			{
				command.Transaction = transaction;
				command.ExecuteNonQuery();
			}

			using (var command = _dbContext.CreateCommand(InsertAppliedMigrationSql,
				("$name", migration.Name),
				("$appliedAt", _timeProvider.GetUtcNow().ToUnixTimeSeconds()),
				("$index", index)))
			{
				command.Transaction = transaction;
				command.ExecuteNonQuery();
			}

			_logger.LogInformation("Migration {Index}: {Name} applied", index, migration.Name);
		}

		transaction.Commit();
	}
}