using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using LauncherKit.Core.Db;
using LauncherKit.Core.Tests.TestSupport;

namespace LauncherKit.Core.Tests.Db;

[TestFixture]
public sealed class DbMigratorTests
{
	[Test]
	public void Migrate_FreshDatabase_AppliesEveryMigrationInOrder()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateGlobalContext();
		var expected = new GlobalMigrationProvider().GetMigrations();
		var sut = CreateSut(new AutoMocker(MockBehavior.Strict), context, expected);

		// Act
		sut.Migrate();

		// Assert
		var applied = ReadAppliedMigrations(context);
		Assert.Multiple(() =>
		{
			Assert.That(applied.Select(x => x.Name), Is.EqualTo(expected.Select(x => x.Name)));
			Assert.That(applied.Select(x => x.Index), Is.EqualTo(Enumerable.Range(0, expected.Count)));
		});
	}

	[Test]
	public void Migrate_AlreadyMigratedDatabase_AppliesNothingMore()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateGlobalContext();
		var migrations = new GlobalMigrationProvider().GetMigrations();
		CreateSut(new AutoMocker(MockBehavior.Strict), context, migrations).Migrate();
		var appliedAfterFirstRun = context.Count("__Migrations");

		// Act
		CreateSut(new AutoMocker(MockBehavior.Strict), context, migrations).Migrate();

		// Assert
		Assert.That(context.Count("__Migrations"), Is.EqualTo(appliedAfterFirstRun));
	}

	[Test]
	public void Migrate_MigrationScriptFails_RollsBackAndRecordsNothing()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateGlobalContext();
		var sut = CreateSut(new AutoMocker(MockBehavior.Strict), context,
		[
			new DbMigration { Name = "CreateValid", Script = "CREATE TABLE \"Valid\" (\"Id\" INTEGER PRIMARY KEY);" },
			new DbMigration { Name = "Broken", Script = "THIS IS NOT SQL;" },
		]);

		// Act
		// Assert
		Assert.That(() => sut.Migrate(), Throws.Exception);
		Assert.Multiple(() =>
		{
			Assert.That(context.Count("__Migrations"), Is.Zero);
			Assert.That(context.TableExists("Valid"), Is.False);
		});
	}

	[Test]
	public void Migrate_NewMigrationAppendedAfterEarlierRun_AppliesOnlyTheNewOne()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateGlobalContext();
		var first = new DbMigration { Name = "First", Script = "CREATE TABLE \"First\" (\"Id\" INTEGER PRIMARY KEY);" };
		var second = new DbMigration { Name = "Second", Script = "CREATE TABLE \"Second\" (\"Id\" INTEGER PRIMARY KEY);" };
		CreateSut(new AutoMocker(MockBehavior.Strict), context, [first]).Migrate();
		var sut = CreateSut(new AutoMocker(MockBehavior.Strict), context, [first, second]);

		// Act
		sut.Migrate();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(ReadAppliedMigrations(context).Select(x => x.Name), Is.EqualTo(new[] { "First", "Second" }));
			Assert.That(context.TableExists("Second"), Is.True);
		});
	}

	[Test]
	public void Migrate_GlobalDatabase_CreatesTablesUsedByTheLauncher()
	{
		// Arrange
		using var database = new SqliteDatabase();

		// Act
		using var context = database.CreateMigratedGlobalContext();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(context.TableExists("Files"), Is.True);
			Assert.That(context.TableExists("KeyValues"), Is.True);
		});
	}

	[Test]
	public void Migrate_SkyrimLocalDatabase_CreatesTablesUsedByTheInstallation()
	{
		// Arrange
		using var database = new SqliteDatabase();

		// Act
		using var context = database.CreateMigratedSkyrimLocalContext();

		// Assert
		Assert.That(context.TableExists("KeyValues"), Is.True);
	}

	private static DbMigrator<GlobalDbContext> CreateSut(AutoMocker mocker, GlobalDbContext context,
		IReadOnlyList<DbMigration> migrations)
	{
		mocker.Use(context);
		mocker.Use<ILogger<DbMigrator<GlobalDbContext>>>(NullLogger<DbMigrator<GlobalDbContext>>.Instance);
		mocker.Use(TimeProvider.System);
		mocker.GetMock<IMigrationProvider<GlobalDbContext>>().Setup(x => x.GetMigrations()).Returns(migrations);
		return mocker.CreateInstance<DbMigrator<GlobalDbContext>>();
	}

	private static List<(string Name, int Index)> ReadAppliedMigrations(GlobalDbContext context) =>
		context.Read("""SELECT "Name", "Index" FROM "__Migrations" ORDER BY "Index" """,
			x => (x.GetString(0), x.GetInt32(1)));
}
