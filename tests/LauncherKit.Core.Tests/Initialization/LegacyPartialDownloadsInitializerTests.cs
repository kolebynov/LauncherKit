using NuGet.Versioning;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.Initialization;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Tests.Initialization;

[TestFixture]
public sealed class LegacyPartialDownloadsInitializerTests
{
	[TestCase("1.18.5", true, Description = "Upgrade from a launcher without file parts")]
	[TestCase("1.19.0-beta.1", false, Description = "Upgrade from the first launcher with file parts")]
	[TestCase("1.19.0", false, Description = "Same version")]
	[TestCase(null, false, Description = "Fresh installation")]
	public async Task Initialize_LastRunVersion_MigratesOnlyPartialFilesFromOlderLaunchers(
		string? lastRunVersion, bool isMigrationExpected)
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IAppVersionProvider>()
			.SetupGet(x => x.LastRunVersion)
			.Returns(lastRunVersion == null ? null : NuGetVersion.Parse(lastRunVersion));
		var storage = mocker.GetMock<IFileStorageService>();
		storage.Setup(x => x.MigrateLegacyPartialDownloads());
		var sut = mocker.CreateInstance<LegacyPartialDownloadsInitializer>(enablePrivate: true);

		// Act
		await sut.Initialize();

		// Assert
		storage.Verify(
			x => x.MigrateLegacyPartialDownloads(), isMigrationExpected ? Times.Once() : Times.Never());
	}
}
