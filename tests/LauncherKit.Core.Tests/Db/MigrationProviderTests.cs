using Microsoft.Extensions.Time.Testing;
using LauncherKit.Core.Db;
using LauncherKit.Core.Tests.TestSupport;

namespace LauncherKit.Core.Tests.Db;

[TestFixture]
public sealed class GlobalMigrationProviderTests
{
	[Test]
	public void GetMigrations_AllScripts_HaveUniqueOrderedNamesAndExpectedSchemaOperations()
	{
		var migrations = new GlobalMigrationProvider().GetMigrations();

		Assert.Multiple(() =>
		{
			Assert.That(migrations.Select(x => x.Name), Is.EqualTo(new[]
			{
				"Initial", "AddSizeColumn", "AddTorrentsTable", "DropTorrentsTable", "AddHashColumn", "AddFileParts",
				"AddFilePartLastWriteAt",
			}));
			Assert.That(migrations.Select(x => x.Name), Is.Unique);
			Assert.That(migrations[0].Script, Does.Contain("CREATE TABLE IF NOT EXISTS \"Files\""));
			Assert.That(migrations[0].Script, Does.Contain("CREATE TABLE IF NOT EXISTS \"KeyValues\""));
			Assert.That(migrations[5].Script, Does.Contain("CREATE TABLE IF NOT EXISTS \"FileParts\""));
		});
	}

	[Test]
	public void GetMigrations_AddFilePartLastWriteAt_BackfillsExistingPartsWithFileCreationTime()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateGlobalContext();
		var migrations = new GlobalMigrationProvider().GetMigrations();
		foreach (var migration in migrations.SkipLast(1))
		{
			context.Execute(migration.Script);
		}

		context.Execute("""INSERT INTO "Files" ("FileName", "Path", "CreatedAt", "Size") VALUES ('payload.bin', 'payload.bin', 1234, 8)""");
		context.Execute("""INSERT INTO "FileParts" ("FileName", "Index", "WrittenBytes", "Size") VALUES ('payload.bin', 0, 4, 8)""");

		// Act
		context.Execute(migrations[^1].Script);

		// Assert
		Assert.That(context.Scalar("""SELECT "LastWriteAt" FROM "FileParts" """), Is.EqualTo(1234));
	}
}

[TestFixture]
public sealed class SkyrimLocalMigrationProviderTests
{
	[Test]
	public void GetMigrations_FixedTime_EmbedsTimestampAndRemovesLegacyFilesTable()
	{
		var now = new DateTimeOffset(2026, 9, 8, 10, 11, 12, TimeSpan.Zero);
		var timeProvider = new FakeTimeProvider(now);

		var migrations = new SkyrimLocalMigrationProvider(timeProvider).GetMigrations();

		Assert.Multiple(() =>
		{
			Assert.That(migrations.Select(x => x.Name), Is.EqualTo(new[]
			{
				"Initial", "AddTimestampColumnsForFiles", "RemoveFilesTable", "AddInstallActionTracking",
			}));
			Assert.That(migrations.Select(x => x.Name), Is.Unique);
			Assert.That(migrations[1].Script, Does.Contain(now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)));
			Assert.That(migrations[2].Script, Does.Contain("DROP TABLE \"Files\""));
		});
	}
}
