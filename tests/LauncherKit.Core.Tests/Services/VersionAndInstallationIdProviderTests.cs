using System.Reflection;
using NuGet.Versioning;
using LauncherKit.Core.Services;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Tests.Services;

[TestFixture]
public sealed class AppVersionProviderTests
{
	[Test]
	public void Constructor_Always_PersistsRunningVersionOnce()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		var storage = mocker.GetMock<IGlobalKeyValueStorage>();
		storage.Setup(x => x.Get("AppVersion")).Returns((string?)null);
		storage.Setup(x => x.Set("AppVersion", It.IsAny<string>()));

		// Act
		var sut = mocker.CreateInstance<AppVersionProvider>();

		// Assert
		storage.Verify(x => x.Set("AppVersion", sut.Version.ToString()), Times.Once);
		Assert.That(sut.Version, Is.EqualTo(NuGetVersion.Parse(
			Assembly.GetEntryAssembly()!.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion)));
	}

	[TestCase("1.18.5", "1.18.5", Description = "Version of the previous run")]
	[TestCase(null, null, Description = "Fresh installation")]
	[TestCase("not-a-version", null, Description = "Unreadable persisted value")]
	public void LastRunVersion_PersistedValue_ReportsPreviouslyRunVersion(string? persisted, string? expected)
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		var storage = mocker.GetMock<IGlobalKeyValueStorage>();
		storage.Setup(x => x.Get("AppVersion")).Returns(persisted);
		storage.Setup(x => x.Set("AppVersion", It.IsAny<string>()));

		// Act
		var sut = mocker.CreateInstance<AppVersionProvider>();

		// Assert
		Assert.That(sut.LastRunVersion?.ToString(), Is.EqualTo(expected));
	}
}

[TestFixture]
public sealed class InstallationIdProviderTests
{
	[Test]
	public void GetInstallationId_PersistedValue_ReturnsWithoutWriting()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IGlobalKeyValueStorage>().Setup(x => x.Get("InstallationId")).Returns("existing-id");
		var sut = mocker.CreateInstance<InstallationIdProvider>();

		// Act
		var result = sut.GetInstallationId();

		// Assert
		Assert.That(result, Is.EqualTo("existing-id"));
	}

	[Test]
	public void GetInstallationId_MissingValue_GeneratesAndPersistsValidGuid()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		var storage = mocker.GetMock<IGlobalKeyValueStorage>();
		storage.Setup(x => x.Get("InstallationId")).Returns((string?)null);
		storage.Setup(x => x.Set("InstallationId", It.IsAny<string>()));
		var sut = mocker.CreateInstance<InstallationIdProvider>();

		// Act
		var result = sut.GetInstallationId();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(Guid.TryParse(result, out _), Is.True);
			storage.Verify(x => x.Set("InstallationId", result), Times.Once);
		});
	}
}
