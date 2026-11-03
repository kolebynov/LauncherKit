using LauncherKit.TestSupport;

namespace LauncherKit.Infrastructure.Tests;

[TestFixture]
public sealed class LaunchersRootDirectoryManagerTests
{
	private const string CurrentDirectory = @"C:\Launchers\1.0.0";
	private const string RegisteredRoot = @"D:\Installed\Launchers";

	[Test]
	public void Directory_UseCurrentDirectory_UsesConsumerRootOfCurrentDirectory()
	{
		// Arrange
		var mocker = ProductTestSupport.CreateMocker();
		mocker.GetMock<IFileSystemUtils>().Setup(x => x.GetCurrentDirectory()).Returns(CurrentDirectory);
		mocker.GetMock<IFileSystemUtils>().Setup(x => x.EnsureDirectoryExists(@"C:\Launchers", true));
		var sut = CreateSut(mocker, true);

		// Act
		var result = sut.Directory;

		// Assert
		Assert.That(result, Is.EqualTo(@"C:\Launchers"));
	}

	[Test]
	public void Directory_UseRegistry_UsesRootRegisteredUnderProductKey()
	{
		// Arrange
		var mocker = ProductTestSupport.CreateMocker();
		var product = mocker.Get<LauncherProductOptions>();
		mocker.GetMock<IRegistryManager>()
			.Setup(x => x.GetValue(product.RegistryKey, LaunchersRootDirectoryManager.RootRegistryValueName))
			.Returns(RegisteredRoot);
		mocker.GetMock<IFileSystemUtils>().Setup(x => x.EnsureDirectoryExists(RegisteredRoot, true));
		var sut = CreateSut(mocker, false);

		// Act
		var result = sut.Directory;

		// Assert
		Assert.That(result, Is.EqualTo(RegisteredRoot));
	}

	[TestCase(null)]
	[TestCase("")]
	public void Directory_UseRegistryWithoutRegisteredRoot_ThrowsWithoutCreatingDirectory(string? registered)
	{
		// Arrange
		var mocker = ProductTestSupport.CreateMocker();
		mocker.GetMock<IRegistryManager>()
			.Setup(x => x.GetValue(It.IsAny<string>(), LaunchersRootDirectoryManager.RootRegistryValueName))
			.Returns(registered);
		var sut = CreateSut(mocker, false);

		// Act + Assert
		Assert.That(() => sut.Directory, Throws.InvalidOperationException);
		mocker.GetMock<IFileSystemUtils>().VerifyNoOtherCalls();
	}

	[Test]
	public void Constructor_InvalidIdentity_ThrowsBeforeAccessingRoot()
	{
		var mocker = ProductTestSupport.CreateMocker();
		mocker.Use(ProductTestSupport.CreateProduct(globalDatabaseFileName: ""));

		Assert.That(() => CreateSut(mocker, true), Throws.ArgumentException);
		mocker.GetMock<IFileSystemUtils>().VerifyNoOtherCalls();
		mocker.GetMock<IRegistryManager>().VerifyNoOtherCalls();
	}

	private static TestRootDirectoryManager CreateSut(AutoMocker mocker, bool useCurrentDirectory) =>
		new(useCurrentDirectory, mocker.Get<IFileSystemUtils>(), mocker.Get<IRegistryManager>(),
			mocker.Get<LauncherProductOptions>());

	// A layout whose root is the parent of the launcher's directory.
	private sealed class TestRootDirectoryManager(
		bool useCurrentDirectory,
		IFileSystemUtils fileSystemUtils,
		IRegistryManager registryManager,
		LauncherProductOptions product)
		: LaunchersRootDirectoryManager(useCurrentDirectory, fileSystemUtils, registryManager, product)
	{
		public override string? TryGetInstalledLauncherDirectory() => throw new NotSupportedException();

		protected override string RootFromCurrentDirectory(string currentDirectory) =>
			Path.GetDirectoryName(currentDirectory)!;
	}
}
