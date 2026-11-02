using LauncherKit.Core.Services;
using LauncherKit.Core.Tests.TestSupport;

namespace LauncherKit.Core.Tests.Services;

[TestFixture]
public sealed class SkyrimLocalKeyValueStorageTests
{
	[Test]
	public void SetGetAndRemove_KeyLifecycle_PersistsExpectedValues()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedSkyrimLocalContext();
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use(context);
		var sut = mocker.CreateInstance<SkyrimLocalKeyValueStorage>(enablePrivate: true);

		// Act and assert
		Assert.That(sut.HasKey("channel"), Is.False);
		Assert.That(sut.Get("channel"), Is.Null);

		sut.Set("channel", "stable");
		Assert.Multiple(() =>
		{
			Assert.That(sut.HasKey("channel"), Is.True);
			Assert.That(sut.Get("channel"), Is.EqualTo("stable"));
		});

		sut.Set("channel", null);
		Assert.Multiple(() =>
		{
			Assert.That(sut.HasKey("channel"), Is.True);
			Assert.That(sut.Get("channel"), Is.Null);
		});

		sut.Remove("channel");
		Assert.Multiple(() =>
		{
			Assert.That(sut.HasKey("channel"), Is.False);
			Assert.That(sut.Get("channel"), Is.Null);
		});
	}
}
