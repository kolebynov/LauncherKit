using LauncherKit.Core.Db;

namespace LauncherKit.Core.Tests.TestSupport;

/// <summary>Assertion helpers that read a test database directly.</summary>
internal static class SqliteDbContextTestExtensions
{
	public static long Scalar(this SqliteDbContext context, string sql) =>
		context.Read(sql, x => x.GetInt64(0)).Single();

	public static long Count(this SqliteDbContext context, string tableName) =>
		context.Scalar($"""SELECT COUNT(*) FROM "{tableName}" """);

	public static bool TableExists(this SqliteDbContext context, string tableName) =>
		context.Read("SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name", _ => true, ("$name", tableName))
			.Count == 1;
}
