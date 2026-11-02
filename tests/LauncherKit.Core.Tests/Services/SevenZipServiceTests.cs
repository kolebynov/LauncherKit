using System.Diagnostics;
using LauncherKit.Core.Services;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Utils;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.Tests.Services;

[TestFixture]
public sealed class SevenZipServiceTests
{
	[Test]
	public async Task ExtractArchive_ArchiveAndDestination_ConstructsListAndExtractCommands()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<ILaunchersRootDirectoryManager>().SetupGet(x => x.Directory).Returns(@"C:\Launcher");
		var runner = mocker.GetMock<IProcessRunner>();
		runner.Setup(x => x.StartProcessAndAssertSuccess(
			It.Is<ProcessStartInfo>(info => info.FileName.EndsWith("7z.exe", StringComparison.Ordinal) &&
				info.Arguments == "l \"archive.7z\" "),
			true,
			CancellationToken.None,
			It.IsAny<Action<string?>?>()))
			.Callback<ProcessStartInfo, bool, CancellationToken, Action<string?>?>((_, _, _, output) =>
			{
				output!("---------- ---------- ---------- ---------- ----------");
				output("---------- ---------- ---------- ---------- ----------");
			})
			.Returns(Task.CompletedTask);
		runner.Setup(x => x.StartProcessAndAssertSuccess(
			It.Is<ProcessStartInfo>(info => info.FileName.EndsWith("7zG.exe", StringComparison.Ordinal) &&
				info.Arguments == "x \"archive.7z\" -o\"destination\" -y -bb0 -bt"),
			true,
			CancellationToken.None,
			null))
			.Returns(Task.CompletedTask);
		mocker.GetMock<IDiskSpaceChecker>()
			.Setup(x => x.CheckAvailableDiskSpace("destination", 0));
		var sut = mocker.CreateInstance<SevenZipService>();

		// Act
		await sut.ExtractArchive("archive.7z", "destination", CancellationToken.None);

		// Assert
		runner.VerifyAll();
	}

	[Test]
	public void ExtractArchive_ListingFails_PropagatesFailureBeforeExtraction()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<ILaunchersRootDirectoryManager>().SetupGet(x => x.Directory).Returns(@"C:\Launcher");
		mocker.GetMock<IProcessRunner>()
			.Setup(x => x.StartProcessAndAssertSuccess(
				It.Is<ProcessStartInfo>(info => info.FileName.EndsWith("7z.exe", StringComparison.Ordinal)),
				true,
				CancellationToken.None,
				It.IsAny<Action<string?>?>()))
			.ThrowsAsync(new InvalidOperationException("listing failed"));
		var sut = mocker.CreateInstance<SevenZipService>();

		// Act
		// Assert
		Assert.That(async () => await sut.ExtractArchive("archive.7z", "destination", CancellationToken.None),
			Throws.InvalidOperationException.With.Message.EqualTo("listing failed"));
	}

	[TestCase(true)]
	[TestCase(false)]
	public async Task ContainsEntry_ListingOfRequestedEntry_ReturnsWhetherEntryWasListed(bool isListed)
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<ILaunchersRootDirectoryManager>().SetupGet(x => x.Directory).Returns(@"C:\Launcher");
		mocker.GetMock<IProcessRunner>()
			.Setup(x => x.StartProcessAndAssertSuccess(
				It.Is<ProcessStartInfo>(info => info.FileName.EndsWith("7z.exe", StringComparison.Ordinal) &&
					info.Arguments == "l \"archive.7z\" \"FOMOD/ModuleConfig.xml\""),
				true,
				CancellationToken.None,
				It.IsAny<Action<string?>?>()))
			.Callback<ProcessStartInfo, bool, CancellationToken, Action<string?>?>((_, _, _, output) =>
			{
				output!("------------------- ----- ------------ ------------  ------------------------");
				if (isListed)
				{
					output("2026-09-26 16:00:36 ....A            4            8  fomod\\ModuleConfig.xml");
				}

				output("------------------- ----- ------------ ------------  ------------------------");
			})
			.Returns(Task.CompletedTask);
		var sut = mocker.CreateInstance<SevenZipService>();

		// Act
		var result = await sut.ContainsEntry("archive.7z", "FOMOD/ModuleConfig.xml", CancellationToken.None);

		// Assert
		Assert.That(result, Is.EqualTo(isListed));
	}
}
