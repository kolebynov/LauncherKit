using Microsoft.Extensions.Logging.Abstractions;
using LauncherKit.Core.Fomod;
using LauncherKit.Core.Services;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Infrastructure;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.Fomod;

[TestFixture]
public sealed class FomodInstallServiceTests
{
	[Test]
	public async Task CreateInstaller_ModuleConfiguration_ParsesTitleAndSteps()
	{
		// Arrange
		using var temp = new TempDirectory();
		var extractionDirectory = temp.Combine("extract");
		Directory.CreateDirectory(extractionDirectory);
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		OptionsStubs.Snapshot(mocker, new AppSettings { SkyrimDirectory = temp.Path });
		mocker.Use<Microsoft.Extensions.Logging.ILogger<LauncherKit.Core.Fomod.FomodInstaller>>(
			NullLogger<LauncherKit.Core.Fomod.FomodInstaller>.Instance);
		var fileSystem = mocker.GetMock<IFileSystemUtils>();
		fileSystem.Setup(x => x.SafeDeleteDirectory(extractionDirectory))
			.Returns(FileSystemUtils.SafeDeleteResult.Success.Instance);
		var modDirectory = Path.Combine(temp.Path, ProductTestSupport.CreateProduct().ModOrganizerDirectoryName, "mods", "Example Mod");
		fileSystem.Setup(x => x.SafeDeleteDirectory(modDirectory))
			.Returns(FileSystemUtils.SafeDeleteResult.NotExist.Instance);
		fileSystem.Setup(x => x.MoveContents(
			It.Is<IEnumerable<string>>(directories => !directories.Any()), modDirectory));
		mocker.GetMock<ITempStorageService>()
			.Setup(x => x.CreateTempDirectory())
			.Returns(new TempStorageService.TempDirectoryHandle(extractionDirectory, fileSystem.Object));
		mocker.GetMock<ISevenZipService>()
			.Setup(x => x.ExtractEntriesFromArchive(
				It.Is<IEnumerable<string>>(paths => paths.SequenceEqual(new[] { "FOMOD/ModuleConfig.xml" })),
				"archive.7z", extractionDirectory, false, CancellationToken.None))
			.Callback(() => File.Copy(
				Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "ModuleConfig.xml"),
				Path.Combine(extractionDirectory, "ModuleConfig.xml"), overwrite: true))
			.Returns(Task.CompletedTask);
		mocker.Use<IComparer<string>>(StringComparer.OrdinalIgnoreCase);
		var sut = mocker.CreateInstance<FomodInstallService>();
		mocker.GetMock<ISevenZipService>()
			.Setup(x => x.ExtractEntriesFromArchive(
				It.Is<IEnumerable<string>>(paths => paths.SequenceEqual(new[] { "1. MD's Graphics Lite - Part 1" })),
				"archive.7z", extractionDirectory, true, CancellationToken.None))
			.Returns(Task.CompletedTask);

		// Act
		var installer = await sut.CreateInstaller("archive.7z", "Example Mod", CancellationToken.None);
		await installer.Install([], CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(installer.Title, Is.EqualTo("Установка MD's Graphics Lite"));
			Assert.That(installer.InstallSteps, Is.Not.Empty);
		});
	}
}
