using NuGet.Versioning;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.Mods;

namespace LauncherKit.Core.Tests.Feed.Mods;

[TestFixture]
public sealed class ModFeedDataTests
{
	[TestCase(false, "4.0.0", true)]
	[TestCase(false, null, true)]
	[TestCase(true, "3.0.0", true)]
	[TestCase(true, "4.0.0", false)]
	[TestCase(true, null, false)]
	public void IsOfferedFor_ModpackVersion_HidesOnlyDiscontinuedWithoutSupportingVersion(bool isDiscontinued,
		string? modpackVersion, bool expected)
	{
		// Arrange
		var sut = CreateMod(isDiscontinued, ("1.0.0", "[3.0.0, 4.0.0)"));

		// Act
		var result = sut.IsOfferedFor(modpackVersion == null ? null : NuGetVersion.Parse(modpackVersion));

		// Assert
		Assert.That(result, Is.EqualTo(expected));
	}

	[TestCase("3.5.0", "1.1.0")]
	[TestCase("4.0.0", null)]
	public void FindLatestSupportedItem_ModpackVersion_ReturnsNewestSupportingVersion(string modpackVersion, string? expected)
	{
		// Arrange
		var sut = CreateMod(false, ("1.0.0", "[3.0.0, 4.0.0)"), ("1.1.0", "[3.0.0, 4.0.0)"), ("0.9.0", "[2.0.0, 3.0.0)"));

		// Act
		var result = sut.FindLatestSupportedItem(NuGetVersion.Parse(modpackVersion));

		// Assert
		Assert.That(result?.Version.ToNormalizedString(), Is.EqualTo(expected));
	}

	private static ModFeedData CreateMod(bool isDiscontinued, params (string Version, string Range)[] items) => new()
	{
		Name = "Mod",
		CategoryId = "mods",
		LogoPath = "logo.jpg",
		DescriptionPath = new() { ["ru"] = "description.md" },
		IsDiscontinued = isDiscontinued,
		Items = items.Select(x => new ModFeedItem
		{
			Version = NuGetVersion.Parse(x.Version),
			ArchiveDownloadInfo = new DownloadInfo { S3Path = "mod/part1.7z" },
			SupportedModpackVersion = VersionRange.Parse(x.Range),
		}).ToArray(),
	};
}
