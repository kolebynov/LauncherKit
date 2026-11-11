using NuGet.Versioning;
using LauncherKit.Core.Choices;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.GraphPacks;
using LauncherKit.Core.Feed.Install;
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
	[TestCase("ChoicesTitle")]
	[TestCase("ChoiceStepTitle")]
	[TestCase("ChoiceStepSubtitle")]
	[TestCase("ChoiceOptionName")]
	[TestCase("ChoiceOptionDescription")]
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

	[Test]
	[SetUICulture("fr-CA")]
	public void Validate_ChoiceTexts_ResolveToFeedDefaultLanguage()
	{
		// Arrange
		var optionName = new LocalizedText { ["en"] = "Option", ["ru"] = "Вариант" };
		var feed = CreateFeed("en", "ChoiceOptionName", optionName);

		// Act
		FeedValidator.Validate(feed);

		// Assert
		Assert.That(optionName.Resolve(), Is.EqualTo("Option"));
	}

	[Test]
	public void Validate_SameOptionIdInTwoChoiceActionsOfOneStep_RejectsFeed()
	{
		// Arrange
		var feed = FeedWithModSteps(new InstallStep { Actions = [Choices("A", "B"), Choices("B")] });

		// Act / Assert
		Assert.That(() => FeedValidator.Validate(feed), Throws.TypeOf<InvalidDataException>());
	}

	[Test]
	public void Validate_SameOptionIdInDifferentSteps_AcceptsFeed()
	{
		// Arrange
		var feed = FeedWithModSteps(new InstallStep { Actions = [Choices("A")] }, new InstallStep { Actions = [Choices("A")] });

		// Act / Assert
		Assert.That(() => FeedValidator.Validate(feed), Throws.Nothing);
	}

	[TestCase(" ")]
	[TestCase("")]
	public void Validate_BlankOptionId_RejectsFeed(string id)
	{
		// Arrange
		var feed = FeedWithModSteps(new InstallStep { Actions = [Choices(id)] });

		// Act / Assert
		Assert.That(() => FeedValidator.Validate(feed), Throws.TypeOf<InvalidDataException>());
	}

	[Test]
	public void Validate_ChoicesWithoutPagesOrOptions_RejectsFeed()
	{
		// Arrange
		var withoutPages = FeedWithModSteps(
			new InstallStep { Actions = [new InstallAction.SelectChoices(new LocalizedText { ["ru"] = "T" }, [])] });
		var withoutOptions = FeedWithModSteps(new InstallStep { Actions = [Choices()] });

		// Act / Assert
		Assert.Multiple(() =>
		{
			Assert.That(() => FeedValidator.Validate(withoutPages), Throws.TypeOf<InvalidDataException>());
			Assert.That(() => FeedValidator.Validate(withoutOptions), Throws.TypeOf<InvalidDataException>());
		});
	}

	private static InstallAction.SelectChoices Choices(params string[] optionIds) =>
		new(new LocalizedText { ["ru"] = "Title" },
		[
			new InstallChoiceStep
			{
				Title = new LocalizedText { ["ru"] = "Page" },
				SelectionMode = ChoiceSelectionMode.Any,
				Options = optionIds.Select(id => new InstallChoiceOption { Id = id, Name = new LocalizedText { ["ru"] = "Option" } }).ToArray(),
			},
		]);

	private static FeedData FeedWithModSteps(params InstallStep[] steps) => CreateFeed("ru", "", new LocalizedText(), steps);

	/// <param name="field">The field set to <paramref name="value"/>; every other text has only the default language.</param>
	/// <param name="modSteps">The mod's install steps; by default one step with a choices action using the texts.</param>
	private static FeedData CreateFeed(string language, string field, LocalizedText value, IReadOnlyList<InstallStep>? modSteps = null)
	{
		LocalizedText Text(string name) => field == name ? value : new() { [language] = name };
		modSteps ??=
		[
			new InstallStep
			{
				Actions =
				[
					new InstallAction.SelectChoices(Text("ChoicesTitle"),
					[
						new InstallChoiceStep
						{
							Title = Text("ChoiceStepTitle"),
							Subtitle = Text("ChoiceStepSubtitle"),
							SelectionMode = ChoiceSelectionMode.ExactlyOne,
							Options =
							[
								new InstallChoiceOption
								{
									Id = "option", Name = Text("ChoiceOptionName"), Description = Text("ChoiceOptionDescription"),
								},
							],
						},
					]),
				],
			},
		];
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
				LogoPath = "mod.png",
				Items =
				[
					new ModFeedItem
					{
						Version = new(1, 0, 0), ArchiveDownloadInfo = new() { S3Path = "mod.7z" },
						SupportedModpackVersion = VersionRange.Parse("[1.0.0,)"), InstallSteps = modSteps,
					},
				],
			}],
			GraphPacks = [new GraphPackFeedData
			{
				Name = "Graphics", DisplayName = Text("GraphPackDisplayName"), DescriptionPath = Text("GraphPackDescription"),
				Recommendation = Text("Recommendation"), LogoPath = "graphics.png", Items = [],
			}],
		};
	}
}
