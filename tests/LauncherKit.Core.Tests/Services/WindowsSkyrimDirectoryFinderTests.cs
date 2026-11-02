using LauncherKit.Core.Services;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Utils;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.Tests.Services;

[TestFixture]
public sealed class WindowsSkyrimDirectoryFinderTests
{
	private const string FirstRegistry = @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
	private const string SecondRegistry = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

	[Test]
	public void FindSkyrimDirectory_FirstValidSkyrimEntry_ReturnsItsInstallLocation()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		var registry = mocker.GetMock<IRegistryManager>();
		registry.Setup(x => x.GetSubKeyNames(FirstRegistry)).Returns(["other", "skyrim", "later"]);
		registry.Setup(x => x.GetValue($@"{FirstRegistry}\other", "DisplayName")).Returns("Another game");
		registry.Setup(x => x.GetValue($@"{FirstRegistry}\skyrim", "DisplayName")).Returns("The Elder Scrolls V: Skyrim Special Edition");
		registry.Setup(x => x.GetValue($@"{FirstRegistry}\skyrim", "InstallLocation")).Returns(@"C:\Games\Skyrim");
		mocker.GetMock<ISkyrimInstallationChecker>()
			.Setup(x => x.CheckSkyrimDirectory(@"C:\Games\Skyrim"))
			.Returns(new SkyrimDirectoryCheckResult.SkyrimDirectory(new Version(1, 6, 1170)));
		var sut = mocker.CreateInstance<WindowsSkyrimDirectoryFinder>();

		// Act
		var result = sut.FindSkyrimDirectory();

		// Assert
		Assert.That(result, Is.EqualTo(@"C:\Games\Skyrim"));
	}

	[Test]
	public void FindSkyrimDirectory_NoMatchingEntry_ReturnsNull()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		var registry = mocker.GetMock<IRegistryManager>();
		registry.Setup(x => x.GetSubKeyNames(FirstRegistry)).Returns(["other"]);
		registry.Setup(x => x.GetValue($@"{FirstRegistry}\other", "DisplayName")).Returns("Another game");
		registry.Setup(x => x.GetSubKeyNames(SecondRegistry)).Returns([]);
		var sut = mocker.CreateInstance<WindowsSkyrimDirectoryFinder>();

		// Act
		var result = sut.FindSkyrimDirectory();

		// Assert
		Assert.That(result, Is.Null);
	}

	[Test]
	public void FindSkyrimDirectory_InvalidSkyrimEntry_ContinuesToNextRegistry()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		var registry = mocker.GetMock<IRegistryManager>();
		registry.Setup(x => x.GetSubKeyNames(FirstRegistry)).Returns(["invalid"]);
		registry.Setup(x => x.GetValue($@"{FirstRegistry}\invalid", "DisplayName")).Returns("Skyrim");
		registry.Setup(x => x.GetValue($@"{FirstRegistry}\invalid", "InstallLocation")).Returns(@"C:\Broken");
		registry.Setup(x => x.GetSubKeyNames(SecondRegistry)).Returns(["valid"]);
		registry.Setup(x => x.GetValue($@"{SecondRegistry}\valid", "DisplayName")).Returns("Skyrim Special Edition");
		registry.Setup(x => x.GetValue($@"{SecondRegistry}\valid", "InstallLocation")).Returns(@"D:\Skyrim");
		var checker = mocker.GetMock<ISkyrimInstallationChecker>();
		checker.Setup(x => x.CheckSkyrimDirectory(@"C:\Broken")).Returns(SkyrimDirectoryCheckResult.NotSkyrimDirectory.Instance);
		checker.Setup(x => x.CheckSkyrimDirectory(@"D:\Skyrim"))
			.Returns(new SkyrimDirectoryCheckResult.SkyrimDirectory(new Version(1, 6, 1170)));
		var sut = mocker.CreateInstance<WindowsSkyrimDirectoryFinder>();

		// Act
		var result = sut.FindSkyrimDirectory();

		// Assert
		Assert.That(result, Is.EqualTo(@"D:\Skyrim"));
	}
}
