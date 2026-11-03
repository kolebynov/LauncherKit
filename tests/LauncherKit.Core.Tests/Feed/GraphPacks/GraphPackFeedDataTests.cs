using NuGet.Versioning;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.GraphPacks;
using LauncherKit.Core.Feed.Mods;

namespace LauncherKit.Core.Tests.Feed.GraphPacks;

[TestFixture]
public sealed class GraphPackFeedDataTests
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
		var sut = new GraphPackFeedData
		{
			Name = "Graph pack",
			LogoPath = "logo.jpg",
			DescriptionPath = new() { ["ru"] = "description.md" },
			Recommendation = new() { ["ru"] = "Recommendation" },
			IsDiscontinued = isDiscontinued,
			Items =
			[
				new ModFeedItem
				{
					Version = NuGetVersion.Parse("1.0.0"),
					ArchiveDownloadInfo = new DownloadInfo { S3Path = "gp/part1.7z" },
					SupportedModpackVersion = VersionRange.Parse("[3.0.0, 4.0.0)"),
				},
			],
		};

		// Act
		var result = sut.IsOfferedFor(modpackVersion == null ? null : NuGetVersion.Parse(modpackVersion));

		// Assert
		Assert.That(result, Is.EqualTo(expected));
	}
}
