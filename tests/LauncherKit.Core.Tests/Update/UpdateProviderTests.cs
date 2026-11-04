using NuGet.Versioning;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.Modpacks;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Update;

namespace LauncherKit.Core.Tests.Update;

[TestFixture]
public sealed class UpdateProviderTests
{
	[Test]
	public async Task GetLauncherUpdateInfo_UnorderedFeed_ReturnsNewestVersionOnlyWhenNewerThanInstalled()
	{
		// Arrange
		var older = Item("1.5.0");
		var newest = Item("3.0.0");
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IFeedProvider>()
			.Setup(x => x.GetLauncherFeedItems(CancellationToken.None))
			.ReturnsAsync([newest, older, Item("2.0.0")]);
		mocker.GetMock<IAppVersionProvider>().SetupGet(x => x.Version).Returns(new NuGetVersion(2, 5, 0));
		var sut = mocker.CreateInstance<UpdateProvider>();

		// Act
		var result = await sut.GetLauncherUpdateInfo(CancellationToken.None);

		// Assert
		Assert.That(result?.NewestLauncher, Is.SameAs(newest));
	}

	[Test]
	public async Task GetLauncherUpdateInfo_InstalledIsNewest_ReturnsNull()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IFeedProvider>()
			.Setup(x => x.GetLauncherFeedItems(CancellationToken.None))
			.ReturnsAsync([Item("2.0.0"), Item("3.0.0", supportsNewModpackVersions: true)]);
		mocker.GetMock<IAppVersionProvider>().SetupGet(x => x.Version).Returns(new NuGetVersion(3, 0, 0));
		var sut = mocker.CreateInstance<UpdateProvider>();

		// Act
		var result = await sut.GetLauncherUpdateInfo(CancellationToken.None);

		// Assert
		Assert.That(result, Is.Null);
	}

	[TestCase("2.6.0", ExpectedResult = true)]
	[TestCase("3.0.0", ExpectedResult = true)]
	[TestCase("2.5.0", ExpectedResult = false)]
	[TestCase("2.0.0", ExpectedResult = false)]
	public async Task<bool> GetLauncherUpdateInfo_FlaggedVersion_SupportsNewModpackVersionsOnlyWhenFlaggedVersionIsNewerThanInstalled(
		string flaggedVersion)
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		IReadOnlyList<LauncherFeedItem> launchers =
		[
			.. new[] { "2.0.0", "2.5.0", "2.6.0", "2.7.0", "3.0.0" }
				.Select(version => Item(version, supportsNewModpackVersions: version == flaggedVersion)),
		];
		mocker.GetMock<IFeedProvider>()
			.Setup(x => x.GetLauncherFeedItems(CancellationToken.None))
			.ReturnsAsync(launchers);
		mocker.GetMock<IAppVersionProvider>().SetupGet(x => x.Version).Returns(new NuGetVersion(2, 5, 0));
		var sut = mocker.CreateInstance<UpdateProvider>();

		// Act
		var result = await sut.GetLauncherUpdateInfo(CancellationToken.None);

		// Assert
		Assert.That(result, Is.Not.Null);
		return result!.SupportsNewModpackVersions;
	}

	[Test]
	public async Task GetAvailableModpackVersions_ItemsWithPatches_ReturnsLatestVersionsNewestFirst()
	{
		// Arrange
		var older = Modpack("3.2.0", "3.2.0", "3.2.2", "3.2.1");
		var newer = Modpack("3.3.0", "3.3.0", "3.3.1");
		var beta = Modpack("3.4.0-beta.1", "3.4.0-beta.1");
		var mocker = CreateModpackMocker(null, older, beta, newer);
		var sut = mocker.CreateInstance<UpdateProvider>();

		// Act
		var result = await sut.GetAvailableModpackVersions(CancellationToken.None);

		// Assert
		Assert.That(result.Select(x => (x.FeedItem, x.LatestVersion.ToString(), x.IsInstalled)),
			Is.EqualTo(new[] { (beta, "3.4.0-beta.1", false), (newer, "3.3.1", false), (older, "3.2.2", false) }));
	}

	[TestCase("3.1.9", ExpectedResult = new[] { false, false })]
	[TestCase("3.2.0", ExpectedResult = new[] { false, true })]
	[TestCase("3.2.1", ExpectedResult = new[] { false, true })]
	[TestCase("3.2.2", ExpectedResult = new[] { false, false })]
	[TestCase("3.3.0", ExpectedResult = new[] { true, false })]
	[TestCase("3.3.2", ExpectedResult = new[] { true, false })]
	[TestCase("3.3.3", ExpectedResult = new[] { false, false })]
	public async Task<bool[]> GetAvailableModpackVersions_InstalledVersion_IsInstalledOnlyFromInitialToLatestVersion(
		string installedVersion)
	{
		// Arrange
		var mocker = CreateModpackMocker(NuGetVersion.Parse(installedVersion),
			Modpack("3.2.0", "3.2.0", "3.2.1"), Modpack("3.3.0", "3.3.1", "3.3.2"));
		var sut = mocker.CreateInstance<UpdateProvider>();

		// Act
		var result = await sut.GetAvailableModpackVersions(CancellationToken.None);

		// Assert
		return [.. result.Select(x => x.IsInstalled)];
	}

	private static AutoMocker CreateModpackMocker(NuGetVersion? installedVersion, params ModpackFeedItem[] modpackFeedItems)
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IFeedProvider>()
			.Setup(x => x.GetModpackFeedItems(CancellationToken.None))
			.ReturnsAsync(modpackFeedItems);
		mocker.GetMock<IModpackVersionProvider>().SetupGet(x => x.InstalledModpackVersion).Returns(installedVersion);
		return mocker;
	}

	private static ModpackFeedItem Modpack(string initialVersion, string version, params string[] patchVersions) => new()
	{
		InitialVersion = NuGetVersion.Parse(initialVersion),
		Version = NuGetVersion.Parse(version),
		DownloadInfo = new DownloadInfo { S3Path = $"modpack-{version}.7z" },
		Patches =
		[
			.. patchVersions.Select(x => new ModpackPatchFeedItem
			{
				Version = NuGetVersion.Parse(x),
				DownloadInfo = new DownloadInfo { S3Path = $"modpack-patch-{x}.7z" },
			}),
		],
	};

	private static LauncherFeedItem Item(string version, bool supportsNewModpackVersions = false) => new()
	{
		Version = NuGetVersion.Parse(version),
		DownloadInfo = new DownloadInfo { S3Path = $"launcher-{version}.zip" },
		SupportsNewModpackVersions = supportsNewModpackVersions,
	};
}
