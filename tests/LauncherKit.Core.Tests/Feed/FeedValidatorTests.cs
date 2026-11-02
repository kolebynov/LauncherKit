using NuGet.Versioning;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.GraphPacks;
using LauncherKit.Core.Feed.Modpacks;
using LauncherKit.Core.Feed.Mods;

namespace LauncherKit.Core.Tests.Feed;

[TestFixture]
public sealed class FeedValidatorTests
{
	[TestCase("ComponentTitle")]
	[TestCase("ComponentDescription")]
	[TestCase("ModDisplayName")]
	[TestCase("ModDescription")]
	[TestCase("GraphPackDisplayName")]
	[TestCase("GraphPackDescription")]
	[TestCase("Recommendation")]
	[TestCase("ModpackChangelog")]
	[TestCase("LauncherChangelog")]
	public void Validate_LocalizedFieldMissingDefaultLanguage_RejectsFeed(string field)
	{
		// Arrange
		var feed = CreateFeed("ru", field, new LocalizedText { ["en"] = "Only English" });

		// Act / Assert
		Assert.That(() => FeedValidator.Validate(feed), Throws.TypeOf<InvalidDataException>());
	}

	[TestCase("")]
	[TestCase(" ")]
	public void Validate_EmptyDefaultLanguage_RejectsFeed(string language)
	{
		// Arrange
		var feed = CreateFeed(language, "ModDisplayName", new LocalizedText { ["ru"] = "Name" });

		// Act / Assert
		Assert.That(() => FeedValidator.Validate(feed), Throws.TypeOf<InvalidDataException>());
	}

	[Test]
	[SetUICulture("fr-CA")]
	public void Validate_IndependentFeeds_KeepsEachFeedsFallbackAfterAnotherFeedLoads()
	{
		// Arrange
		var firstText = new LocalizedText { ["ru"] = "Первый", ["en"] = "First" };
		var secondText = new LocalizedText { ["ru"] = "Второй", ["en"] = "Second" };
		var first = CreateFeed("ru", "ModDisplayName", firstText);
		var second = CreateFeed("en", "ModDisplayName", secondText);

		// Act
		FeedValidator.Validate(first);
		FeedValidator.Validate(second);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(firstText.Resolve(), Is.EqualTo("Первый"));
			Assert.That(secondText.Resolve(), Is.EqualTo("Second"));
		});
	}

	private static FeedData CreateFeed(string language, string field, LocalizedText value)
	{
		LocalizedText Text(string name) => field == name ? value : new() { [language] = name };
		return new FeedData
		{
			DefaultLanguage = language,
			SkyrimDownloadInfo = new() { S3Path = "skyrim.7z" },
			SevenZipDownloadInfo = new() { S3Path = "7z.zip" },
			Modpacks = [],
			ModpackChangelogs = [new ModpackChangelogFeedItem { Version = new(1, 0, 0), Path = Text("ModpackChangelog") }],
			Launchers = [new LauncherFeedItem
			{
				Version = new(1, 0, 0), DownloadInfo = new() { S3Path = "launcher.zip" }, ChangelogPath = Text("LauncherChangelog"),
			}],
			InstallComponents = [new InstallComponent
			{
				Id = "graphics", Kind = InstallComponentKind.GraphPack,
				Title = Text("ComponentTitle"), Description = Text("ComponentDescription"),
				SupportedModpackVersion = VersionRange.Parse("[1.0.0,)"),
			}],
			Mods = [new ModFeedData
			{
				Name = "Mod", DisplayName = Text("ModDisplayName"), DescriptionPath = Text("ModDescription"),
				LogoPath = "mod.png", Items = [],
			}],
			GraphPacks = [new GraphPackFeedData
			{
				Name = "Graphics", DisplayName = Text("GraphPackDisplayName"), DescriptionPath = Text("GraphPackDescription"),
				Recommendation = Text("Recommendation"), LogoPath = "graphics.png", Items = [],
			}],
		};
	}
}
