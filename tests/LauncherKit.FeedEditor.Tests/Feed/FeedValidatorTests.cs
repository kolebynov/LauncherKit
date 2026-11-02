using System.Text.Json.Nodes;
using LauncherKit.FeedEditor.Feed;
using LauncherKit.FeedEditor.Feed.Json;

namespace LauncherKit.FeedEditor.Tests.Feed;

[TestFixture]
public sealed class FeedValidatorTests
{
	[Test]
	public void Validate_StoredFeed_ReturnsNoErrors()
	{
		// Arrange
		var feed = FeedJson.Deserialize(FeedFixtures.StoredFeedJson());

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result, Is.Empty);
	}

	[Test]
	public void Validate_ValuesTheLauncherCannotParse_ReportsEachWithLocation()
	{
		// Arrange
		var feed = FeedJson.Deserialize(FeedFixtures.StoredFeedJson());
		feed.Launchers[0].Version = "next";
		feed.SkyrimDownloadInfo.Hash = "ABC";
		feed.GraphPacks[1].Items[0].SupportedModpackVersion = "3.1 to 3.2";
		feed.ModpackChangelogs[0].Path["ru"] = " ";

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result.Select(x => x.Location), Is.EquivalentTo(new[]
		{
			"Лаунчер next › Версия",
			"Загрузка Skyrim › Хеш",
			"Графпак MD's Graphics Full 1.17.0 › Поддерживаемые версии сборки",
			"Список изменений сборки 3.1.6 › Путь › ru",
		}));
	}

	[TestCase(1)]
	[TestCase(2)]
	[TestCase(-1)]
	public void Validate_ConditionNotOnEarlierStep_ReportsError(int conditionStepIndex)
	{
		// Arrange
		var feed = FeedWithModSteps(
			new ModInstallStepModel { Actions = { new SelectRequiredModAction { Mods = { "A" } } } },
			new ModInstallStepModel
			{
				Actions = { new ActivateModsAction { Mods = { "A" } } },
				Condition = new ModInstallConditionModel { StepIndex = conditionStepIndex, Value = "A" },
			});

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result.Select(x => x.Location), Is.EqualTo(new[] { "Мод M 1.0.0 › Шаг 1 › Условие" }));
	}

	[Test]
	public void Validate_ConditionOnEarlierStep_ReturnsNoErrors()
	{
		// Arrange
		var feed = FeedWithModSteps(
			new ModInstallStepModel { Actions = { new SelectRequiredModAction { Mods = { "A" } } } },
			new ModInstallStepModel
			{
				Actions = { new ActivateModsAction { Mods = { "A" } } },
				Condition = new ModInstallConditionModel { StepIndex = 0, Value = "A" },
			});

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result, Is.Empty);
	}

	[Test]
	public void Validate_ActionWithoutRequiredList_ReportsError()
	{
		// Arrange
		var feed = FeedWithModSteps(new ModInstallStepModel { Actions = { new ActivatePluginsAction() } });

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result.Select(x => x.Location), Is.EqualTo(new[] { "Мод M 1.0.0 › Шаг 0 › Действие 1 › Плагины" }));
	}

	[Test]
	public void Validate_ModItemWithoutSteps_ReturnsNoErrors()
	{
		// Arrange
		var feed = FeedWithModSteps();

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result, Is.Empty);
	}

	[Test]
	public void Validate_PatchStepRemovingNoPaths_ReportsError()
	{
		// Arrange
		var feed = FeedWithModSteps();
		feed.Modpacks.Add(new ModpackModel
		{
			InitialVersion = "3.1.0",
			Version = "3.1.0",
			DownloadInfo = new DownloadInfoModel { S3Path = "stb.7z" },
			Patches =
			{
				new ModpackPatchModel
				{
					Version = "3.1.1",
					DownloadInfo = new DownloadInfoModel { S3Path = "patch.7z" },
					InstallSteps = { new ModInstallStepModel { Actions = { new RemoveFileSystemItemAction() } } },
				},
			},
		});

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result.Select(x => x.Location), Is.EqualTo(new[] { "Сборка 3.1.0 › Патч 3.1.1 › Шаг 0 › Действие 1 › Пути" }));
	}

	[Test]
	public void Validate_ModItemWithoutArchivePath_ReportsMissingPath()
	{
		// Arrange
		var feed = FeedWithModSteps();
		feed.Mods[0].Items[0].ArchiveDownloadInfo.S3Path = " ";

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result.Select(x => x.Location), Is.EqualTo(new[] { "Мод M 1.0.0 › Архив мода › Путь S3" }));
	}

	[Test]
	public void Validate_InstallModOfAnotherModNextToMod_ReturnsNoErrors()
	{
		// Arrange
		var feed = FeedWithModSteps(new ModInstallStepModel
		{
			Actions =
			{
				new InstallModAction
				{
					DownloadInfo = new DownloadInfoModel { S3Path = "Mods/b.7z" }, ModName = "B",
					Placement = new ModPlacementModel { Kind = ModPlacementKind.NextToMod, ModName = "A", Side = ModListSide.Above },
				},
			},
		});

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result, Is.Empty);
	}

	[Test]
	public void Validate_InstallModWithoutPathAndModName_ReportsBoth()
	{
		// Arrange
		var feed = FeedWithModSteps(new ModInstallStepModel { Actions = { new InstallModAction() } });

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result.Select(x => x.Location), Is.EquivalentTo(new[]
		{
			"Мод M 1.0.0 › Шаг 0 › Действие 1 › Путь S3",
			"Мод M 1.0.0 › Шаг 0 › Действие 1 › Имя мода",
		}));
	}

	[TestCase("M")]
	[TestCase("m")]
	public void Validate_InstallModOfTheModItself_ReportsError(string modName)
	{
		// Arrange
		var feed = FeedWithModSteps(new ModInstallStepModel
		{
			Actions = { new InstallModAction { DownloadInfo = new DownloadInfoModel { S3Path = "Mods/m.7z" }, ModName = modName } },
		});

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result.Select(x => x.Location), Is.EqualTo(new[] { "Мод M 1.0.0 › Шаг 0 › Действие 1 › Имя мода" }));
	}

	[TestCase("")]
	[TestCase("Extras_separator")]
	[TestCase("Extras_SEPARATOR")]
	[TestCase("A/B")]
	[TestCase("A:B")]
	[TestCase("A?")]
	public void Validate_ModVersionUnderInvalidSeparator_ReportsError(string separator)
	{
		// Arrange
		var feed = FeedWithModSteps();
		feed.Mods[0].Items[0].Placement = new ModPlacementModel { Kind = ModPlacementKind.UnderSeparator, Separator = separator };

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result.Select(x => x.Location), Is.EqualTo(new[] { "Мод M 1.0.0 › Размещение › Разделитель" }));
	}

	[TestCase("", "Мод M 1.0.0 › Шаг 0 › Действие 1 › Размещение › Мод")]
	[TestCase("b", "Мод M 1.0.0 › Шаг 0 › Действие 1 › Размещение › Мод")]
	public void Validate_InstallModNextToMissingOrItself_ReportsError(string neighbor, string expectedLocation)
	{
		// Arrange
		var feed = FeedWithModSteps(new ModInstallStepModel
		{
			Actions =
			{
				new InstallModAction
				{
					DownloadInfo = new DownloadInfoModel { S3Path = "Mods/b.7z" }, ModName = "B",
					Placement = new ModPlacementModel { Kind = ModPlacementKind.NextToMod, ModName = neighbor },
				},
			},
		});

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result.Select(x => x.Location), Is.EqualTo(new[] { expectedLocation }));
	}

	[Test]
	public void Validate_SameVersionWrittenDifferently_ReportsDuplicate()
	{
		// Arrange
		var feed = FeedJson.Deserialize(FeedFixtures.StoredFeedJson());
		feed.Launchers.Add(new LauncherModel { Version = "1.19", DownloadInfo = new DownloadInfoModel { S3Path = "l.exe" } });

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result, Is.EqualTo(new[] { new FeedValidationError("Лаунчеры", "Версия 1.19.0 указана больше одного раза.") }));
	}

	[Test]
	public void Validate_ModNamesDifferingOnlyInCase_ReportsDuplicate()
	{
		// Arrange
		var feed = FeedJson.Deserialize(FeedFixtures.StoredFeedJson());
		var copy = FeedJson.Clone(feed.Mods[0]);
		copy.Name = feed.Mods[0].Name.ToUpperInvariant();
		feed.Mods.Add(copy);

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result.Select(x => x.Location), Is.EqualTo(new[] { "Моды" }));
	}

	[Test]
	public void Validate_SeveralDefaultGraphPacksWithSameName_ReportsBoth()
	{
		// Arrange
		var feed = FeedJson.Deserialize(FeedFixtures.StoredFeedJson());
		feed.GraphPacks[1].IsDefault = true;
		feed.GraphPacks[2].Name = "md's graphics lite";

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result.Select(x => x.Message), Is.EquivalentTo(new[]
		{
			"Имя графпака «MD's Graphics Lite» используется больше одного раза.",
			"Выбранным по умолчанию может быть только один графпак, не снятый с поддержки.",
		}));
	}

	[Test]
	public void Validate_GraphPackArchiveWithoutPath_ReportsMissingPath()
	{
		// Arrange
		var feed = FeedJson.Deserialize(FeedFixtures.StoredFeedJson());
		feed.GraphPacks[2].Items[0].ArchiveDownloadInfo.S3Path = " ";

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result.Select(x => x.Location),
			Is.EqualTo(new[] { "Графпак Single Archive Graphics 1.0.0 › Архив мода › Путь S3" }));
	}

	[TestCase(0, "1.5.0", null)]
	[TestCase(0, "2.0.0", "Версия 2.0.0 уже указана как версия мода.")]
	[TestCase(1, "1.0.0", "Версия 1.0.0 уже указана как версия мода.")]
	[TestCase(0, "0.9.0", "Версия патча должна быть выше версии мода.")]
	[TestCase(0, "2.1.0", "Версия патча должна быть ниже следующей версии мода 2.0.0.")]
	public void Validate_GraphPackPatchVersion_ReportsVersionsOutsideItsModVersion(int itemIndex, string patchVersion,
		string? expectedError)
	{
		// Arrange
		var feed = FeedJson.Deserialize(FeedFixtures.StoredFeedJson());
		var graphPack = feed.GraphPacks[2];
		graphPack.Items.Add(new ModItemModel
		{
			Version = "2.0.0",
			ArchiveDownloadInfo = new DownloadInfoModel { S3Path = "gp-2.0.0.7z" },
			SupportedModpackVersion = "[3.2.0, )",
		});
		graphPack.Items[itemIndex].Patches.Add(new ModPatchModel
		{
			Version = patchVersion,
			DownloadInfo = new DownloadInfoModel { S3Path = "gp-patch.7z" },
		});

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result.Select(x => x.Message), Is.EqualTo(expectedError == null ? Array.Empty<string>() : new[] { expectedError }));
	}

	[TestCase("[3.2.0-beta.1, 3.2.0)")]
	[TestCase("[3.3.0-beta.1, 3.4.0)")]
	public void Validate_SponsorPrereleaseWithRole_ReturnsNoErrorsWithOrWithoutBetaCode(string betaRange)
	{
		// Arrange
		var feed = FeedWithSponsorModpack("3.2.0-beta.1", betaRange, "197038439483310086");

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result, Is.Empty);
	}

	[Test]
	public void Validate_SponsorVersionNotAPrerelease_ReportsError()
	{
		// Arrange
		var feed = FeedWithSponsorModpack("3.2.0", "[3.2.0-beta.1, 3.3.0)", "197038439483310086");

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result.Select(x => x.Location), Is.EqualTo(new[] { "Сборка 3.2.0 › Доступна спонсорам" }));
	}

	[Test]
	public void Validate_SponsorVersionWithoutSponsorRoles_ReportsMissingRoles()
	{
		// Arrange
		var feed = FeedWithSponsorModpack("3.2.0-beta.1", "[3.2.0-beta.1, 3.2.0)");

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result.Select(x => x.Location), Is.EqualTo(new[] { "Роли спонсоров" }));
	}

	[TestCase("sponsor")]
	[TestCase("-1")]
	[TestCase("0")]
	public void Validate_SponsorRoleIdNotASnowflake_ReportsError(string roleId)
	{
		// Arrange
		var feed = FeedWithSponsorModpack("3.2.0-beta.1", "[3.2.0-beta.1, 3.2.0)", roleId);

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result.Select(x => x.Location), Is.EqualTo(new[] { "Роль спонсора #1 › ID" }));
	}

	[Test]
	public void Validate_InvalidAndDuplicateDeveloperRoleIds_ReportsEach()
	{
		// Arrange
		var feed = FeedWithSponsorModpack("3.2.0-beta.1", "[3.2.0-beta.1, 3.2.0)", "197038439483310086");
		feed.DeveloperRoleIds.AddRange(["41771983423143936", "41771983423143936", "developer"]);

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result.Select(x => x.Location), Is.EquivalentTo(new[] { "Роли разработчиков", "Роль разработчика #3 › ID" }));
	}

	private static FeedModel FeedWithModSteps(params ModInstallStepModel[] steps)
	{
		var item = new ModItemModel
		{
			Version = "1.0.0",
			ArchiveDownloadInfo = new DownloadInfoModel { S3Path = "Mods/m.7z" },
			SupportedModpackVersion = "[1.0.0, 2.0.0)",
		};
		item.InstallSteps.AddRange(steps);
		return new FeedModel
		{
			DefaultLanguage = "ru",
			SkyrimDownloadInfo = new DownloadInfoModel { S3Path = "Skyrim.7z" },
			SevenZipDownloadInfo = new DownloadInfoModel { S3Path = "7z.zip" },
			Mods = { new ModModel { Name = "M", LogoPath = "l", DescriptionPath = new Dictionary<string, string> { ["ru"] = "d" }, Items = { item } } },
		};
	}

	private static FeedModel FeedWithSponsorModpack(string version, string betaRange, params string[] sponsorRoleIds)
	{
		var feed = new FeedModel
		{
			DefaultLanguage = "ru",
			SkyrimDownloadInfo = new DownloadInfoModel { S3Path = "Skyrim.7z" },
			SevenZipDownloadInfo = new DownloadInfoModel { S3Path = "7z.zip" },
			Modpacks =
			{
				new ModpackModel
				{
					InitialVersion = version, Version = version, DownloadInfo = new DownloadInfoModel { S3Path = "stb.7z" },
					IsAvailableForSponsors = true,
				},
			},
			BetaVersions = { new BetaVersionModel { Hash = new string('A', FeedRules.Sha256HexLength), VersionRange = betaRange } },
		};
		feed.SponsorRoleIds.AddRange(sponsorRoleIds);
		return feed;
	}
}
