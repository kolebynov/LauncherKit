using LauncherKit.Core.Services;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.Tests.Services;

[TestFixture]
public sealed class InstallDetectorTests
{
	[TestCase(43, false)]
	[TestCase(44, true)]
	public void IsVcRedistInstalled_MinorVersionBoundary_ReturnsExpected(int minor, bool expected)
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		var registry = mocker.GetMock<IRegistryManager>();
		registry.Setup(x => x.GetValue(It.Is<string>(key => key.EndsWith("X64", StringComparison.Ordinal)), "Installed")).Returns(1);
		registry.Setup(x => x.GetValue(It.Is<string>(key => key.EndsWith("X64", StringComparison.Ordinal)), "Major")).Returns(14);
		registry.Setup(x => x.GetValue(It.Is<string>(key => key.EndsWith("X64", StringComparison.Ordinal)), "Minor")).Returns(minor);
		var sut = mocker.CreateInstance<InstallDetector>();

		// Act
		var result = sut.IsVcRedistInstalled(InstallDetector.VcRedistSupportedArchitecture.X64.Instance);

		// Assert
		Assert.That(result, Is.EqualTo(expected));
	}

	[Test]
	public void IsVcRedistInstalled_MissingInstalledValue_ReturnsFalseWithoutReadingVersions()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IRegistryManager>()
			.Setup(x => x.GetValue(It.IsAny<string>(), "Installed"))
			.Returns((object?)null);
		var sut = mocker.CreateInstance<InstallDetector>();

		// Act
		var result = sut.IsVcRedistInstalled(InstallDetector.VcRedistSupportedArchitecture.X86.Instance);

		// Assert
		Assert.That(result, Is.False);
	}

	[Test]
	public void IsVcRedistInstalled_X86Architecture_ReadsX86RegistryPath()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		var registry = mocker.GetMock<IRegistryManager>();
		registry.Setup(x => x.GetValue(It.Is<string>(key => key.EndsWith("X86", StringComparison.Ordinal)), "Installed")).Returns(1);
		registry.Setup(x => x.GetValue(It.Is<string>(key => key.EndsWith("X86", StringComparison.Ordinal)), "Major")).Returns(14);
		registry.Setup(x => x.GetValue(It.Is<string>(key => key.EndsWith("X86", StringComparison.Ordinal)), "Minor")).Returns(44);
		var sut = mocker.CreateInstance<InstallDetector>();

		// Act
		var result = sut.IsVcRedistInstalled(InstallDetector.VcRedistSupportedArchitecture.X86.Instance);

		// Assert
		Assert.That(result, Is.True);
	}
}
