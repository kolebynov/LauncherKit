using System.Text.Json;
using System.Text.Json.Nodes;
using LauncherKit.Core;
using LauncherKit.FeedEditor.Feed;
using LauncherKit.FeedEditor.Feed.Json;

namespace LauncherKit.FeedEditor.Tests.Feed.Json;

[TestFixture]
public sealed class FeedV12ContractTests
{
	[Test]
	public void Serialize_V12Fixture_WritesGenericNamesComponentsAndPrerequisites()
	{
		var feed = FeedJson.Deserialize(FeedFixtures.StoredFeedJson());
		var result = JsonNode.Parse(FeedJson.Serialize(feed))!.AsObject();
		Assert.Multiple(() =>
		{
			Assert.That(result.ContainsKey("Stb"), Is.False);
			Assert.That(result.ContainsKey("StbChangelogs"), Is.False);
			Assert.That(result["Modpacks"]!.AsArray(), Is.Not.Empty);
			Assert.That(result["ModpackChangelogs"]!.AsArray(), Is.Not.Empty);
			Assert.That(result["DefaultLanguage"]!.GetValue<string>(), Is.EqualTo("ru"));
			Assert.That(result["InstallComponents"]![0]!["Kind"]!.GetValue<string>(), Is.EqualTo("GraphPack"));
			Assert.That(result["InstallComponents"]![1]!["Kind"]!.GetValue<string>(), Is.EqualTo("Mod"));
			Assert.That(result["InstallComponents"]![1]!["ModName"]!.GetValue<string>(), Is.EqualTo("WispShelterStb"));
			Assert.That(result["Prerequisites"]![0]!["Id"]!.GetValue<string>(), Is.EqualTo("directx9"));
			Assert.That(result["Prerequisites"]![0]!["Arguments"]!.GetValue<string>(), Is.EqualTo("/silent"));
			Assert.That(result.ToJsonString(), Does.Not.Contain("SupportedStbVersion").And.Not.Contain("SupportsNewStbVersions"));
		});
	}

	[Test]
	public void Serialize_AllLocalizedContent_LauncherReadsEveryLanguageAndComponentField()
	{
		var feed = FeedJson.Deserialize(FeedFixtures.StoredFeedJson());
		feed.Mods[0].DisplayName = new Dictionary<string, string> { ["ru"] = "Мод", ["en"] = "Mod" };
		feed.Mods[0].DescriptionPath["en"] = "Mods/description.en.md";
		feed.GraphPacks[0].Recommendation["en"] = "4 GB VRAM";
		feed.GraphPacks[0].DescriptionPath["en"] = "GraphPacks/description.en.md";
		feed.ModpackChangelogs[0].Path["en"] = "Changelogs/modpack.en.md";
		feed.Launchers[0].ChangelogPath = new Dictionary<string, string> { ["ru"] = "launcher.ru.md", ["en"] = "launcher.en.md" };
		var editorJson = FeedJson.Serialize(feed);
		var runtimeFeed = JsonSerializer.Deserialize(editorJson, SourceGeneratedJsonContext.Default.FeedData)!;
		var launcher = JsonNode.Parse(JsonSerializer.Serialize(runtimeFeed, SourceGeneratedJsonContext.Default.FeedData))!;
		var editor = JsonNode.Parse(editorJson)!;

		Assert.Multiple(() =>
		{
			Assert.That(launcher["DefaultLanguage"]!.GetValue<string>(), Is.EqualTo("ru"));
			Assert.That(JsonNode.DeepEquals(launcher["Mods"]![0]!["DisplayName"], editor["Mods"]![0]!["DisplayName"]), Is.True);
			Assert.That(JsonNode.DeepEquals(launcher["Mods"]![0]!["DescriptionPath"], editor["Mods"]![0]!["DescriptionPath"]), Is.True);
			Assert.That(JsonNode.DeepEquals(launcher["GraphPacks"]![0]!["Recommendation"], editor["GraphPacks"]![0]!["Recommendation"]), Is.True);
			Assert.That(JsonNode.DeepEquals(launcher["GraphPacks"]![0]!["DescriptionPath"], editor["GraphPacks"]![0]!["DescriptionPath"]), Is.True);
			Assert.That(JsonNode.DeepEquals(launcher["ModpackChangelogs"]![0]!["Path"], editor["ModpackChangelogs"]![0]!["Path"]), Is.True);
			Assert.That(JsonNode.DeepEquals(launcher["Launchers"]![0]!["ChangelogPath"], editor["Launchers"]![0]!["ChangelogPath"]), Is.True);
			Assert.That(launcher["Prerequisites"]![0]!["DownloadInfo"]!["S3Path"]!.GetValue<string>(), Is.EqualTo("Prerequisites/dxwebsetup.exe"));
			Assert.That(launcher["Prerequisites"]![0]!["Arguments"]!.GetValue<string>(), Is.EqualTo("/silent"));
			foreach (var componentIndex in Enumerable.Range(0, feed.InstallComponents.Count))
			{
				foreach (var field in new[] { "Id", "Kind", "ModName", "Title", "Description", "SupportedModpackVersion", "Order" })
				{
					Assert.That(JsonNode.DeepEquals(launcher["InstallComponents"]![componentIndex]![field],
						editor["InstallComponents"]![componentIndex]![field]), Is.True, $"Component {componentIndex}, {field}");
				}
			}
		});
	}

	[Test]
	public void Clone_LocalizedComponent_DoesNotShareLanguageMaps()
	{
		var original = FeedJson.Deserialize(FeedFixtures.StoredFeedJson()).InstallComponents[0];
		var clone = FeedJson.Clone(original);
		clone.Title["en"] = "Changed title";
		clone.Description["en"] = "Changed description";
		Assert.Multiple(() =>
		{
			Assert.That(original.Title["en"], Is.EqualTo("Graphics pack"));
			Assert.That(original.Description["en"], Is.EqualTo("Install a graphics pack."));
		});
	}

	[TestCase("component-title")]
	[TestCase("component-description")]
	[TestCase("mod-description")]
	[TestCase("mod-display-name")]
	[TestCase("graph-recommendation")]
	[TestCase("graph-description")]
	[TestCase("modpack-changelog")]
	[TestCase("launcher-changelog")]
	public void Validate_LocalizedFieldMissingDefaultLanguage_RejectsFeed(string field)
	{
		var feed = FeedJson.Deserialize(FeedFixtures.StoredFeedJson());
		var values = LocalizedField(feed, field);
		values.Clear();
		values["en"] = "Only English";
		Assert.That(FeedValidator.Validate(feed).Select(error => error.Message),
			Has.Some.Contains("языка по умолчанию"));
	}

	[TestCase("")]
	[TestCase(" ")]
	public void Validate_BlankNonDefaultTranslation_ReportsLanguageWithoutDroppingIt(string translation)
	{
		var feed = FeedJson.Deserialize(FeedFixtures.StoredFeedJson());
		feed.InstallComponents[0].Title["en"] = translation;
		Assert.That(FeedValidator.Validate(feed).Select(error => error.Location),
			Does.Contain("Компонент graph-pack › Заголовок › en"));
		var roundTrip = FeedJson.Deserialize(FeedJson.Serialize(feed));
		Assert.That(roundTrip.InstallComponents[0].Title["en"], Is.EqualTo(translation));
	}

	[Test]
	public void Validate_DefaultLanguageChanges_RequiresNewDefaultInEveryMap()
	{
		var feed = FeedJson.Deserialize(FeedFixtures.StoredFeedJson());
		feed.DefaultLanguage = "en";
		Assert.That(FeedValidator.Validate(feed).Select(error => error.Message),
			Has.Some.Contains("«en»"));
	}

	[TestCase("", "Фид › Язык по умолчанию")]
	[TestCase("/invalid/", "Фид › Язык по умолчанию")]
	public void Validate_InvalidDefaultLanguage_ReportsRootLanguage(string language, string location)
	{
		var feed = FeedJson.Deserialize(FeedFixtures.StoredFeedJson());
		feed.DefaultLanguage = language;
		Assert.That(FeedValidator.Validate(feed).Select(error => error.Location), Does.Contain(location));
	}

	[Test]
	public void Validate_EmptyOptionalLocalizedMap_IsNotEquivalentToAbsentMap()
	{
		var feed = FeedJson.Deserialize(FeedFixtures.StoredFeedJson());
		feed.Mods[0].DisplayName = null;
		feed.Launchers[0].ChangelogPath = null;
		Assert.That(FeedValidator.Validate(feed), Is.Empty);
		feed.Mods[0].DisplayName = new Dictionary<string, string>();
		feed.Launchers[0].ChangelogPath = new Dictionary<string, string>();
		Assert.That(FeedValidator.Validate(feed).Select(error => error.Location), Is.EquivalentTo(new[]
		{
			$"Мод {feed.Mods[0].Name} › Отображаемое имя",
			$"Лаунчер {feed.Launchers[0].Version} › Список изменений",
		}));
	}

	[Test]
	public void Validate_ComponentsAndPrerequisites_RejectsUnknownReferencesDuplicatesAndInvalidDownloads()
	{
		var feed = FeedJson.Deserialize(FeedFixtures.StoredFeedJson());
		feed.InstallComponents[1].ModName = "Missing mod";
		feed.InstallComponents[1].SupportedModpackVersion = "not a version range";
		feed.InstallComponents.Add(FeedJson.Clone(feed.InstallComponents[0]));
		feed.Prerequisites[0].DownloadInfo.S3Path = " ";
		feed.Prerequisites[0].DownloadInfo.Hash = "ABC";
		feed.Prerequisites.Add(FeedJson.Clone(feed.Prerequisites[0]));
		var errors = FeedValidator.Validate(feed);
		Assert.Multiple(() =>
		{
			Assert.That(errors.Select(error => error.Location), Does.Contain("Компоненты").And.Contain("Зависимости"));
			Assert.That(errors.Select(error => error.Location), Does.Contain("Компонент wisp-shelter › Поддерживаемые версии сборки"));
			Assert.That(errors.Select(error => error.Message), Does.Contain("Указанный мод отсутствует в фиде."));
			Assert.That(errors.Select(error => error.Location), Does.Contain("Зависимость directx9 › Путь S3").And.Contain("Зависимость directx9 › Хеш"));
		});
	}

	[TestCase("mod-display-name")]
	[TestCase("launcher-changelog")]
	public void Validate_WhitespaceOptionalTranslation_ReportsErrorInsteadOfOmittingIt(string field)
	{
		var feed = FeedJson.Deserialize(FeedFixtures.StoredFeedJson());
		LocalizedField(feed, field)["ru"] = " ";
		Assert.That(FeedValidator.Validate(feed), Is.Not.Empty);
	}

	[Test]
	public void Validate_UndeclaredDefaultLanguage_DoesNotAssumeRussian()
	{
		var json = JsonNode.Parse(FeedFixtures.StoredFeedJson())!.AsObject();
		json.Remove("DefaultLanguage");
		var feed = FeedJson.Deserialize(json.ToJsonString());
		Assert.That(FeedValidator.Validate(feed).Select(error => error.Location),
			Does.Contain("Фид › Язык по умолчанию"));
	}

	[Test]
	public void Validate_TranslationLanguageKeys_RejectsInvalidAndCaseDuplicateKeys()
	{
		var feed = FeedJson.Deserialize(FeedFixtures.StoredFeedJson());
		feed.InstallComponents[0].Title["/invalid/"] = "Invalid language";
		feed.InstallComponents[0].Title["EN"] = "Duplicate English";
		var errors = FeedValidator.Validate(feed);
		Assert.Multiple(() =>
		{
			Assert.That(errors.Select(error => error.Location), Does.Contain("Компонент graph-pack › Заголовок › Язык"));
			Assert.That(errors.Select(error => error.Message), Does.Contain("Язык указан больше одного раза."));
		});
	}

	[Test]
	public void Validate_GraphPackComponentWithModReference_RejectsReference()
	{
		var feed = FeedJson.Deserialize(FeedFixtures.StoredFeedJson());
		feed.InstallComponents[0].ModName = feed.Mods[0].Name;
		Assert.That(FeedValidator.Validate(feed).Select(error => error.Message),
			Does.Contain("Выбор графпака не должен ссылаться на мод."));
	}

	[Test]
	public void Validate_UnknownComponentKind_RejectsKind()
	{
		var feed = FeedJson.Deserialize(FeedFixtures.StoredFeedJson());
		feed.InstallComponents[0].Kind = (InstallComponentKind)99;
		Assert.That(FeedValidator.Validate(feed).Select(error => error.Message),
			Does.Contain("Неизвестный тип компонента."));
	}

	[Test]
	public void Validate_PrerequisiteArguments_EmptyIsValidButNullIsRejected()
	{
		var feed = FeedJson.Deserialize(FeedFixtures.StoredFeedJson());
		feed.Prerequisites[0].Arguments = string.Empty;
		Assert.That(FeedValidator.Validate(feed), Is.Empty);
		feed.Prerequisites[0].Arguments = null!;
		Assert.That(FeedValidator.Validate(feed).Select(error => error.Message),
			Does.Contain("Аргументы не могут быть null."));
	}

	private static Dictionary<string, string> LocalizedField(FeedModel feed, string field)
	{
		if (field is "mod-display-name" or "launcher-changelog")
		{
			var values = new Dictionary<string, string> { ["ru"] = "Text" };
			if (field == "mod-display-name")
			{
				feed.Mods[0].DisplayName = values;
			}
			else
			{
				feed.Launchers[0].ChangelogPath = values;
			}
			return values;
		}

		return field switch
		{
			"component-title" => feed.InstallComponents[0].Title,
			"component-description" => feed.InstallComponents[0].Description,
			"mod-description" => feed.Mods[0].DescriptionPath,
			"graph-recommendation" => feed.GraphPacks[0].Recommendation,
			"graph-description" => feed.GraphPacks[0].DescriptionPath,
			"modpack-changelog" => feed.ModpackChangelogs[0].Path,
			_ => throw new ArgumentOutOfRangeException(nameof(field)),
		};
	}
}
