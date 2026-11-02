using LauncherKit.TestSupport;

namespace LauncherKit.Infrastructure.Tests;

[TestFixture]
public sealed class LaunchersRootDirectoryManagerTests
{
	[Test]
	public void TryGetInstalledLauncherDirectory_VersionFile_ReturnsDirectoryNamedByVersion()
	{
		// Arrange
		using var temp = new TempDirectory();
		var child = temp.CreateDirectory("current");
		temp.CreateFile(".version", "2.3.4-beta.1");
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IFileSystemUtils>().Setup(x => x.EnsureDirectoryExists(temp.Path, true));
		var sut = CreateSut(mocker, true, child);

		// Act
		var result = sut.TryGetInstalledLauncherDirectory();

		// Assert
		Assert.That(result, Is.EqualTo(temp.Combine("2.3.4-beta.1")));
	}

	[Test]
	public void TryGetInstalledLauncherDirectory_MissingVersionFile_ReturnsNull()
	{
		// Arrange
		using var temp = new TempDirectory();
		var child = temp.CreateDirectory("current");
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IFileSystemUtils>().Setup(x => x.EnsureDirectoryExists(temp.Path, true));
		var sut = CreateSut(mocker, true, child);

		// Act
		var result = sut.TryGetInstalledLauncherDirectory();

		// Assert
		Assert.That(result, Is.Null);
	}

	[Test]
	public void TryGetInstalledLauncherDirectory_GarbageVersionFile_Throws()
	{
		// Arrange
		using var temp = new TempDirectory();
		var child = temp.CreateDirectory("current");
		temp.CreateFile(".version", "not-a-version");
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IFileSystemUtils>().Setup(x => x.EnsureDirectoryExists(temp.Path, true));
		var sut = CreateSut(mocker, true, child);

		// Act + Assert
		Assert.That(sut.TryGetInstalledLauncherDirectory, Throws.InstanceOf<InvalidOperationException>());
	}

	[Test]
	public void Constructor_UseCurrentDirectoryDisabled_UsesLocalApplicationData()
	{
		// Arrange
		var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExampleLauncher");
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IFileSystemUtils>().Setup(x => x.EnsureDirectoryExists(expected, true));

		// Act
		var sut = CreateSut(mocker, false, @"Z:\ignored\child");

		// Assert
		Assert.That(sut.Directory, Is.EqualTo(expected));
	}

	[Test]
	public void IndependentIdentities_UseSeparateRootStateDirectories()
	{
		var first = ProductTestSupport.CreateProduct();
		var second = ProductTestSupport.CreateProduct();
		second.RootDirectoryName = "OtherLauncher";
		var mocker = ProductTestSupport.CreateMocker();
		var fileSystem = mocker.GetMock<IFileSystemUtils>();
		var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		fileSystem.Setup(x => x.EnsureDirectoryExists(Path.Combine(basePath, first.RootDirectoryName), true));
		fileSystem.Setup(x => x.EnsureDirectoryExists(Path.Combine(basePath, second.RootDirectoryName), true));

		mocker.Use(false);
		mocker.Use("unused");
		mocker.Use(first);
		var firstManager = mocker.CreateInstance<LaunchersRootDirectoryManager>();
		mocker.Use(second);
		var secondManager = mocker.CreateInstance<LaunchersRootDirectoryManager>();

		Assert.That(firstManager.Directory, Is.Not.EqualTo(secondManager.Directory));
	}

	[Test]
	public void InvalidIdentity_ThrowsBeforeCreatingRootStateDirectory()
	{
		var mocker = ProductTestSupport.CreateMocker();
		var fileSystem = mocker.GetMock<IFileSystemUtils>();
		mocker.Use(false);
		mocker.Use("unused");
		mocker.Use(new LauncherProductOptions());
		Assert.That(() => mocker.CreateInstance<LaunchersRootDirectoryManager>(),
			Throws.Exception.Matches<Exception>(error => error?.GetBaseException() is ArgumentException));
		fileSystem.VerifyNoOtherCalls();
	}

	private static LaunchersRootDirectoryManager CreateSut(
		AutoMocker mocker,
		bool useCurrentDirectory,
		string currentDirectory)
	{
		mocker.Use(useCurrentDirectory);
		mocker.Use(currentDirectory);
		mocker.Use(ProductTestSupport.CreateProduct());
		return mocker.CreateInstance<LaunchersRootDirectoryManager>();
	}
}
