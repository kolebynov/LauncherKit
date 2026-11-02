using FomodInstaller.Scripting.XmlScript;
using Microsoft.Extensions.Logging.Abstractions;
using LauncherKit.Core.Fomod;
using LauncherKit.Core.Services;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Infrastructure;
using LauncherKit.TestSupport;
using Sut = LauncherKit.Core.Fomod.FomodInstaller;

namespace LauncherKit.Core.Tests.Fomod;

[TestFixture]
public sealed class FomodInstallerTests
{
	[Test]
	public async Task Install_SelectedOption_ExtractsRequiredAndSelectedFoldersThenMovesInComparerOrder()
	{
		using var temp = new TempDirectory();
		var extractionDirectory = temp.CreateDirectory("extract");
		var z = Directory.CreateDirectory(Path.Combine(extractionDirectory, "z")).FullName;
		var a = Directory.CreateDirectory(Path.Combine(extractionDirectory, "a")).FullName;
		var script = LoadScript(File.ReadAllText(FixturePath));
		var selectedStep = script.InstallSteps[1];
		var selectedOption = selectedStep.OptionGroups[0].Options[0];
		var mocker = new AutoMocker(MockBehavior.Strict);
		var fileSystem = mocker.GetMock<IFileSystemUtils>();
		mocker.GetMock<ITempStorageService>().Setup(x => x.CreateTempDirectory())
			.Returns(new TempStorageService.TempDirectoryHandle(extractionDirectory, fileSystem.Object));
		mocker.GetMock<ISevenZipService>()
			.Setup(x => x.ExtractEntriesFromArchive(
				It.Is<IEnumerable<string>>(paths => paths.SequenceEqual(new[] { "1. MD's Graphics Lite - Part 1", "31. AW" })),
				"archive.7z", extractionDirectory, true, CancellationToken.None))
			.Returns(Task.CompletedTask);
		var modDirectory = Path.Combine(temp.Path, "mods", "Example");
		fileSystem.Setup(x => x.SafeDeleteDirectory(modDirectory)).Returns(FileSystemUtils.SafeDeleteResult.NotExist.Instance);
		fileSystem.Setup(x => x.MoveContents(It.Is<IEnumerable<string>>(paths => paths.SequenceEqual(new[] { a, z })), modDirectory));
		fileSystem.Setup(x => x.SafeDeleteDirectory(extractionDirectory)).Returns(FileSystemUtils.SafeDeleteResult.Success.Instance);
		var sut = new Sut(
			mocker.GetMock<ISevenZipService>().Object, mocker.GetMock<ITempStorageService>().Object,
			NullLogger<Sut>.Instance, fileSystem.Object, StringComparer.Ordinal,
			script, "archive.7z", temp.Path, "Example");

		await sut.Install([new ConfiguredInstallStep { Step = selectedStep, SelectedOptions = [selectedOption] }], CancellationToken.None);

		mocker.VerifyAll();
	}

	[TestCase("<folder source=\"1. MD's Graphics Lite - Part 1\" destination=\"\"/>",
		"<file source=\"payload.txt\" destination=\"\"/>", "Only folders are supported")]
	[TestCase("<folder source=\"1. MD's Graphics Lite - Part 1\" destination=\"\"/>",
		"<folder source=\"payload\" destination=\"../outside\"/>", "Destination is not supported")]
	public void Install_UnsupportedRequiredFile_ThrowsBeforeExtraction(string oldXml, string newXml, string message)
	{
		using var temp = new TempDirectory();
		var extractionDirectory = temp.CreateDirectory("extract");
		var script = LoadScript(File.ReadAllText(FixturePath).Replace(oldXml, newXml, StringComparison.Ordinal));
		var mocker = new AutoMocker(MockBehavior.Strict);
		var fileSystem = mocker.GetMock<IFileSystemUtils>();
		mocker.GetMock<ITempStorageService>().Setup(x => x.CreateTempDirectory())
			.Returns(new TempStorageService.TempDirectoryHandle(extractionDirectory, fileSystem.Object));
		fileSystem.Setup(x => x.SafeDeleteDirectory(extractionDirectory)).Returns(FileSystemUtils.SafeDeleteResult.Success.Instance);
		var sut = new Sut(
			mocker.GetMock<ISevenZipService>().Object, mocker.GetMock<ITempStorageService>().Object,
			NullLogger<Sut>.Instance, fileSystem.Object, StringComparer.Ordinal,
			script, "archive.7z", temp.Path, "Example");

		Assert.That(async () => await sut.Install([], CancellationToken.None),
			Throws.TypeOf<NotSupportedException>().With.Message.EqualTo(message));
		mocker.GetMock<ISevenZipService>().VerifyNoOtherCalls();
	}

	private static string FixturePath => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "ModuleConfig.xml");

	private static XmlScript LoadScript(string xml) => new XmlScriptType().LoadScript(xml, true);
}
