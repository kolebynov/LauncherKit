using LauncherKit.Core.Utils;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.Utils;

[TestFixture]
public sealed class SkyrimInstallationCheckerTests
{
	[TestCase(null)]
	[TestCase("")]
	public void CheckSkyrimDirectory_NullOrEmptyPath_ReturnsNotSkyrimDirectory(string? path)
	{
		// Arrange
		var sut = new AutoMocker(MockBehavior.Strict).CreateInstance<SkyrimInstallationChecker>();

		// Act
		var result = sut.CheckSkyrimDirectory(path);

		// Assert
		Assert.That(result is SkyrimDirectoryCheckResult.NotSkyrimDirectory, Is.True);
	}

	[Test]
	public void CheckSkyrimDirectory_DirectoryWithoutExecutable_ReturnsNotSkyrimDirectory()
	{
		// Arrange
		using var temp = new TempDirectory();
		var sut = new AutoMocker(MockBehavior.Strict).CreateInstance<SkyrimInstallationChecker>();

		// Act
		var result = sut.CheckSkyrimDirectory(temp.Path);

		// Assert
		Assert.That(result is SkyrimDirectoryCheckResult.NotSkyrimDirectory, Is.True);
	}

	[Test]
	public void CheckSkyrimDirectory_VersionedExecutable_ReturnsItsProductVersionString()
	{
		// Arrange
		using var temp = new TempDirectory();
		temp.CreateFile("SkyrimSE.exe", VersionResourcePeFile.Create(new Version(1, 0, 0, 0), "1.5.97.0"));
		var sut = new AutoMocker(MockBehavior.Strict).CreateInstance<SkyrimInstallationChecker>();

		// Act
		var result = sut.CheckSkyrimDirectory(temp.Path);

		// Assert
		Assert.That(GetVersion(result), Is.EqualTo(new Version(1, 5, 97, 0)));
	}

	[Test]
	public void CheckSkyrimDirectory_NonNumericProductVersion_FallsBackToNumericVersionFields()
	{
		// Arrange
		using var temp = new TempDirectory();
		temp.CreateFile("SkyrimSE.exe", VersionResourcePeFile.Create(new Version(1, 6, 1170, 0), "1.6.1170.0 beta"));
		var sut = new AutoMocker(MockBehavior.Strict).CreateInstance<SkyrimInstallationChecker>();

		// Act
		var result = sut.CheckSkyrimDirectory(temp.Path);

		// Assert
		Assert.That(GetVersion(result), Is.EqualTo(new Version(1, 6, 1170, 0)));
	}

	private static Version? GetVersion(SkyrimDirectoryCheckResult result) => result switch
	{
		SkyrimDirectoryCheckResult.NotSkyrimDirectory => null,
		SkyrimDirectoryCheckResult.SkyrimDirectory(var version) => version,
	};
}
