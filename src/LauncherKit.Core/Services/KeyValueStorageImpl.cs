using LauncherKit.Core.Db;

namespace LauncherKit.Core.Services;

internal static class KeyValueStorageImpl
{
	private const string HasKeySql =
		$"""SELECT 1 FROM "{DbConstants.KeyValuesTableName}" WHERE "Key" = $key LIMIT 1""";

	private const string GetSql =
		$"""SELECT "Value" FROM "{DbConstants.KeyValuesTableName}" WHERE "Key" = $key LIMIT 1""";

	private const string SetSql =
		$"""
		 INSERT INTO "{DbConstants.KeyValuesTableName}" ("Key", "Value") VALUES ($key, $value)
		 ON CONFLICT ("Key") DO UPDATE SET "Value" = excluded."Value"
		 """;

	private const string RemoveSql = $"""DELETE FROM "{DbConstants.KeyValuesTableName}" WHERE "Key" = $key""";

	public static bool HasKey(SqliteDbContext dbContext, string key) =>
		dbContext.Read(HasKeySql, _ => true, ("$key", key)).Count > 0;

	public static string? Get(SqliteDbContext dbContext, string key) =>
		dbContext.Read(GetSql, x => x.IsDBNull(0) ? null : x.GetString(0), ("$key", key)).FirstOrDefault();

	public static void Set(SqliteDbContext dbContext, string key, string? value) =>
		dbContext.Execute(SetSql, ("$key", key), ("$value", value));

	public static void Remove(SqliteDbContext dbContext, string key) => dbContext.Execute(RemoveSql, ("$key", key));
}
