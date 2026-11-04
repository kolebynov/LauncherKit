using NuGet.Versioning;
using LauncherKit.Core.Services;
using LauncherKit.Infrastructure;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.Services;

[TestFixture]
public sealed class ModpackVersionProviderTests
{
	[Test]
	public void Constructor_MissingVersionFile_ReportsNoInstalledVersion()
	{
		using var temp = new TempDirectory();
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		OptionsStubs.Monitor(mocker, new AppSettings { SkyrimDirectory = temp.Path });

		var sut = mocker.CreateInstance<ModpackVersionProvider>(enablePrivate: true);

		Assert.That(sut.InstalledModpackVersion, Is.Null);
	}

	[Test]
	public void RefreshInstalledModpackVersion_UnchangedVersion_DoesNotRaiseDuplicateChange()
	{
		using var temp = new TempDirectory();
		var versionFile = Path.Combine(temp.Path, ProductTestSupport.CreateProduct().ModpackVersionFile);
		Directory.CreateDirectory(Path.GetDirectoryName(versionFile)!);
		File.WriteAllText(versionFile, "4.2.1-beta.3");
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		OptionsStubs.Monitor(mocker, new AppSettings { SkyrimDirectory = temp.Path });
		var sut = mocker.CreateInstance<ModpackVersionProvider>(enablePrivate: true);
		var changes = 0;
		sut.PropertyChanged += (_, args) =>
		{
			if (args.PropertyName == nameof(sut.InstalledModpackVersion))
			{
				changes++;
			}
		};

		sut.RefreshInstalledModpackVersion();

		Assert.Multiple(() =>
		{
			Assert.That(sut.InstalledModpackVersion, Is.EqualTo(NuGetVersion.Parse("4.2.1-beta.3")));
			Assert.That(changes, Is.Zero);
		});
	}

	[Test]
	public void SettingsChange_NewInstallationDirectory_RefreshesVersionAndRaisesPropertyChanged()
	{
		using var first = new TempDirectory();
		using var second = new TempDirectory();
		var versionFile = Path.Combine(second.Path, ProductTestSupport.CreateProduct().ModpackVersionFile);
		Directory.CreateDirectory(Path.GetDirectoryName(versionFile)!);
		File.WriteAllText(versionFile, "5.0.0");
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		var monitor = OptionsStubs.Monitor(mocker, new AppSettings { SkyrimDirectory = first.Path });
		var sut = mocker.CreateInstance<ModpackVersionProvider>(enablePrivate: true);
		var changes = 0;
		sut.PropertyChanged += (_, _) => changes++;

		monitor.Change(new AppSettings { SkyrimDirectory = second.Path });

		Assert.Multiple(() =>
		{
			Assert.That(sut.InstalledModpackVersion, Is.EqualTo(NuGetVersion.Parse("5.0.0")));
			Assert.That(changes, Is.EqualTo(1));
		});
	}
}
