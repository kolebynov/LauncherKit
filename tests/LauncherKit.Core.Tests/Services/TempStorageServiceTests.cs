using LauncherKit.Core.Services;
using LauncherKit.Infrastructure;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.Services;

[TestFixture]
public sealed class TempStorageServiceTests
{
	[Test]
	public void CreateTempDirectory_Twice_CreatesUniqueDirectories()
	{
		// Arrange
		using var temp = new TempDirectory();
		var mocker = new AutoMocker(MockBehavior.Strict);
		OptionsStubs.Monitor(mocker, new AppSettings { AppDataDirectory = temp.Path });
		var fileSystem = mocker.GetMock<IFileSystemUtils>();
		fileSystem.Setup(x => x.EnsureDirectoryExists(It.IsAny<string>(), true));
		var sut = mocker.CreateInstance<TempStorageService>();

		// Act
		var first = sut.CreateTempDirectory();
		var second = sut.CreateTempDirectory();

		// Assert
		Assert.That(second.Path, Is.Not.EqualTo(first.Path));
	}

	[Test]
	public void TempDirectoryHandle_Dispose_DeletesOwnedDirectory()
	{
		// Arrange
		using var temp = new TempDirectory();
		var mocker = new AutoMocker(MockBehavior.Strict);
		OptionsStubs.Monitor(mocker, new AppSettings { AppDataDirectory = temp.Path });
		var fileSystem = mocker.GetMock<IFileSystemUtils>();
		fileSystem.Setup(x => x.EnsureDirectoryExists(It.IsAny<string>(), true));
		fileSystem.Setup(x => x.SafeDeleteDirectory(It.IsAny<string>()))
			.Returns(FileSystemUtils.SafeDeleteResult.Success.Instance);
		var sut = mocker.CreateInstance<TempStorageService>();
		var handle = sut.CreateTempDirectory();

		// Act
		handle.Dispose();

		// Assert
		fileSystem.Verify(x => x.SafeDeleteDirectory(handle.Path), Times.Once);
	}

	[Test]
	public void DeleteTempData_SettingsChange_DeletesNewRoot()
	{
		// Arrange
		using var first = new TempDirectory();
		using var second = new TempDirectory();
		var mocker = new AutoMocker(MockBehavior.Strict);
		var settings = OptionsStubs.Monitor(mocker, new AppSettings { AppDataDirectory = first.Path });
		var expected = Path.Combine(second.Path, "Temp");
		var fileSystem = mocker.GetMock<IFileSystemUtils>();
		fileSystem.Setup(x => x.SafeDeleteDirectory(expected)).Returns(FileSystemUtils.SafeDeleteResult.Success.Instance);
		var sut = mocker.CreateInstance<TempStorageService>();

		// Act
		settings.Change(new AppSettings { AppDataDirectory = second.Path });
		sut.DeleteTempData();

		// Assert
		fileSystem.Verify(x => x.SafeDeleteDirectory(expected), Times.Once);
	}
}
