using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using LauncherKit.Core.Initialization;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Infrastructure;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.Initialization;

[TestFixture]
public sealed class DataDirectoriesRegistrationInitializerTests
{
	private static readonly string Key = ProductTestSupport.CreateProduct().RegistryKey;

	[Test]
	public async Task Initialize_DataDirectorySet_RegistersItsFolders()
	{
		// Arrange
		var mocker = ProductTestSupport.CreateMocker();
		OptionsStubs.Monitor(mocker, new AppSettings { AppDataDirectory = @"D:\STBData\" });
		var registry = mocker.GetMock<IRegistryManager>();
		registry.Setup(x => x.SetValue(Key, It.IsAny<string>(), It.IsAny<object>()));
		using var sut = CreateSut(mocker);

		// Act
		await sut.Initialize();

		// Assert
		registry.Verify(x => x.SetValue(Key, "DownloadsDirectory", @"D:\STBData\Downloads"), Times.Once);
		registry.Verify(x => x.SetValue(Key, "TempDataDirectory", @"D:\STBData\Temp"), Times.Once);
		registry.Verify(x => x.SetValue(Key, "AppDataDirectory", @"D:\STBData"), Times.Once);
	}

	[Test]
	public async Task Initialize_DataDirectoryIsDriveRoot_RegistersOnlyItsFolders()
	{
		// Arrange
		var mocker = ProductTestSupport.CreateMocker();
		OptionsStubs.Monitor(mocker, new AppSettings { AppDataDirectory = @"D:\" });
		var registry = mocker.GetMock<IRegistryManager>();
		registry.Setup(x => x.SetValue(Key, It.IsAny<string>(), It.IsAny<object>()));
		using var sut = CreateSut(mocker);

		// Act
		await sut.Initialize();

		// Assert
		registry.Verify(x => x.SetValue(Key, "DownloadsDirectory", @"D:\Downloads"), Times.Once);
		registry.Verify(x => x.SetValue(Key, "TempDataDirectory", @"D:\Temp"), Times.Once);
		registry.Verify(x => x.SetValue(Key, "AppDataDirectory", It.IsAny<object>()), Times.Never);
	}

	[Test]
	public async Task Initialize_DataDirectoryNotSetThenChanged_RegistersTheNewFolders()
	{
		// Arrange
		var mocker = ProductTestSupport.CreateMocker();
		var monitor = OptionsStubs.Monitor(mocker, new AppSettings());
		var registry = mocker.GetMock<IRegistryManager>();
		registry.Setup(x => x.SetValue(Key, It.IsAny<string>(), It.IsAny<object>()));
		using var sut = CreateSut(mocker);
		await sut.Initialize();
		registry.Verify(x => x.SetValue(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()), Times.Never);

		// Act
		monitor.Change(new AppSettings { AppDataDirectory = @"E:\Data" });

		// Assert
		registry.Verify(x => x.SetValue(Key, "DownloadsDirectory", @"E:\Data\Downloads"), Times.Once);
	}

	[Test]
	public async Task Initialize_RegistryWriteFails_DoesNotThrow()
	{
		// Arrange
		var mocker = ProductTestSupport.CreateMocker();
		OptionsStubs.Monitor(mocker, new AppSettings { AppDataDirectory = @"D:\STBData" });
		mocker.GetMock<IRegistryManager>()
			.Setup(x => x.SetValue(Key, It.IsAny<string>(), It.IsAny<object>()))
			.Throws(new UnauthorizedAccessException());
		using var sut = CreateSut(mocker);

		// Act + Assert
		Assert.That(async () => await sut.Initialize(), Throws.Nothing);
	}

	private static DataDirectoriesRegistrationInitializer CreateSut(AutoMocker mocker)
	{
		mocker.Use<ILogger<DataDirectoriesRegistrationInitializer>>(NullLogger<DataDirectoriesRegistrationInitializer>.Instance);
		return mocker.CreateInstance<DataDirectoriesRegistrationInitializer>(enablePrivate: true);
	}
}
