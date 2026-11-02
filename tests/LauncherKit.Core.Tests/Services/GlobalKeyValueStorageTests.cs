using Microsoft.Extensions.DependencyInjection;
using LauncherKit.Core.Services;
using LauncherKit.Core.Tests.TestSupport;

namespace LauncherKit.Core.Tests.Services;

[TestFixture]
public sealed class GlobalKeyValueStorageTests
{
	[Test]
	public void SetGetOverwriteAndRemove_KeyLifecycle_PersistsAcrossScopes()
	{
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var provider = new ServiceCollection().AddSingleton(context).BuildServiceProvider();
		using var scope = new GlobalScope(provider);
		var sut = new GlobalKeyValueStorage(scope);

		Assert.That(sut.Get("channel"), Is.Null);
		sut.Set("channel", "stable");
		Assert.That(sut.Get("channel"), Is.EqualTo("stable"));
		sut.Set("channel", "beta");
		Assert.Multiple(() =>
		{
			Assert.That(sut.HasKey("channel"), Is.True);
			Assert.That(sut.Get("channel"), Is.EqualTo("beta"));
		});
		sut.Remove("channel");
		Assert.Multiple(() =>
		{
			Assert.That(sut.HasKey("channel"), Is.False);
			Assert.That(sut.Get("channel"), Is.Null);
		});
	}
}
