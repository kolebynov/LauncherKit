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
		feed.Mods[1].Items[0].SupportedModpackVersion = "3.1 to 3.2";
		feed.ModpackChangelogs[0].Path["ru"] = " ";

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result.Select(x => x.Location), Is.EquivalentTo(new[]
		{
			"Лаунчер next › Версия",
			"Загрузка Skyrim › Хеш",
			"Мод MD's Graphics Full 1.17.0 › Поддерживаемые версии сборки",
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

	[TestCase(SelectChoicesAction.Selected)]
	[TestCase(SelectChoicesAction.NotSelected)]
	public void Validate_ConditionOnChoiceOption_ReturnsNoErrors(string value)
	{
		// Arrange
		var feed = FeedWithModSteps(
			new ModInstallStepModel { Actions = { Choices(ChoiceSelectionMode.Any, Option("A"), Option("B")) } },
			new ModInstallStepModel
			{
				Actions = { new ActivateModsAction { Mods = { "A" } } },
				Condition = new ModInstallConditionModel { StepIndex = 0, Key = "B", Value = value },
			});

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result, Is.Empty);
	}

	[Test]
	public void Validate_ConditionOnChoiceOptionWithOtherValue_ReportsValue()
	{
		// Arrange
		var feed = FeedWithModSteps(
			new ModInstallStepModel { Actions = { Choices(ChoiceSelectionMode.Any, Option("A")) } },
			new ModInstallStepModel
			{
				Actions = { new ActivateModsAction { Mods = { "A" } } },
				Condition = new ModInstallConditionModel { StepIndex = 0, Key = "A", Value = "A" },
			});

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result.Select(x => x.Location), Is.EqualTo(new[] { "Мод M 1.0.0 › Шаг 1 › Условие › Значение" }));
	}

	[Test]
	public void Validate_ConditionKeyTheStepDoesNotSet_ReportsKey()
	{
		// Arrange
		var feed = FeedWithModSteps(
			new ModInstallStepModel { Actions = { Choices(ChoiceSelectionMode.Any, Option("A")) } },
			new ModInstallStepModel
			{
				Actions = { new ActivateModsAction { Mods = { "A" } } },
				Condition = new ModInstallConditionModel { StepIndex = 0, Value = "A" },
			});

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result.Select(x => x.Location), Is.EqualTo(new[] { "Мод M 1.0.0 › Шаг 1 › Условие › Ключ" }));
	}

	[Test]
	public void Validate_StepSettingKeyTwice_ReportsStep()
	{
		// Arrange
		var feed = FeedWithModSteps(
			new ModInstallStepModel
			{
				Actions =
				{
					new SelectRequiredModAction { Mods = { "A" } },
					Choices(ChoiceSelectionMode.Any, Option(ModInstallConditionModel.SelectedModKey), Option("B")),
					Choices(ChoiceSelectionMode.Any, Option("B")),
				},
			});

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result.Select(x => x.Location), Is.EqualTo(new[] { "Мод M 1.0.0 › Шаг 0", "Мод M 1.0.0 › Шаг 0" }));
	}

	[Test]
	public void Validate_IncompleteChoices_ReportsEachMissingPart()
	{
		// Arrange
		var emptyPage = new ChoiceStepModel { Title = new Dictionary<string, string> { ["ru"] = "Страница" } };
		var feed = FeedWithModSteps(new ModInstallStepModel
		{
			Actions =
			{
				new SelectChoicesAction(),
				new SelectChoicesAction { Title = new Dictionary<string, string> { ["ru"] = "Выбор" }, Steps = { emptyPage } },
				Choices(ChoiceSelectionMode.Any, new ChoiceOptionModel()),
			},
		});

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result.Select(x => x.Location), Is.EquivalentTo(new[]
		{
			"Мод M 1.0.0 › Шаг 0 › Действие 1 › Заголовок окна",
			"Мод M 1.0.0 › Шаг 0 › Действие 1 › Страницы",
			"Мод M 1.0.0 › Шаг 0 › Действие 2 › Страница 1 › Варианты",
			"Мод M 1.0.0 › Шаг 0 › Действие 3 › Страница 1 › Вариант #1 › Идентификатор",
			"Мод M 1.0.0 › Шаг 0 › Действие 3 › Страница 1 › Вариант #1 › Название",
		}));
	}

	[TestCase(ChoiceSelectionMode.ExactlyOne, 1)]
	[TestCase(ChoiceSelectionMode.AtMostOne, 1)]
	[TestCase(ChoiceSelectionMode.AtLeastOne, 0)]
	[TestCase(ChoiceSelectionMode.Any, 0)]
	public void Validate_TwoRequiredOptions_ReportsOnlyForSingleSelection(ChoiceSelectionMode mode, int expectedErrors)
	{
		// Arrange
		var first = Option("A");
		var second = Option("B");
		first.IsRequired = second.IsRequired = true;
		var feed = FeedWithModSteps(new ModInstallStepModel { Actions = { Choices(mode, first, second) } });

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result, Has.Count.EqualTo(expectedErrors));
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
			DownloadInfo = new DownloadInfoModel { S3Path = "modpack.7z" },
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
	public void Validate_ModChoiceWithTwoDefaults_ReportsComponent()
	{
		// Arrange
		var feed = FeedJson.Deserialize(FeedFixtures.StoredFeedJson());
		feed.InstallComponents[0].Options[1].IsDefault = true;

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result, Is.EqualTo(new[]
		{
			new FeedValidationError("Компонент graph-pack › Варианты", "Выбранным по умолчанию может быть только один мод."),
		}));
	}

	[Test]
	public void Validate_ModChoiceWithRepeatedAndMissingMods_ReportsEach()
	{
		// Arrange
		var feed = FeedJson.Deserialize(FeedFixtures.StoredFeedJson());
		var options = feed.InstallComponents[0].Options;
		options.Add(new ModChoiceOptionModel { ModName = options[0].ModName });
		options.Add(new ModChoiceOptionModel { ModName = "Missing" });

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result.Select(x => x.Message), Is.EquivalentTo(new[]
		{
			"Мод «MD's Graphics Lite» предложен больше одного раза.",
			"Указанный мод отсутствует в фиде.",
		}));
	}

	[Test]
	public void Validate_ModChoiceWithoutOptions_ReportsMissingOptions()
	{
		// Arrange
		var feed = FeedJson.Deserialize(FeedFixtures.StoredFeedJson());
		feed.InstallComponents[0].Options.Clear();

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result.Select(x => x.Location), Is.EqualTo(new[] { "Компонент graph-pack › Варианты" }));
	}

	[Test]
	public void Validate_SingleModChoiceOfferingExclusiveCategoryMods_ReportsNothing()
	{
		// Arrange
		var feed = FeedJson.Deserialize(FeedFixtures.StoredFeedJson());

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(feed.InstallComponents[0].Options.Select(x => feed.Mods.Single(m => m.Name == x.ModName).CategoryId),
				Is.EqualTo(new[] { "graph-packs", "graph-packs", "graph-packs" }));
			Assert.That(result, Is.Empty);
		});
	}

	[TestCase(InstallComponentKind.ModChoice)]
	[TestCase(InstallComponentKind.Mod)]
	public void Validate_ExclusiveCategoryModsInTwoComponents_ReportsConflict(InstallComponentKind kind)
	{
		// Arrange
		var feed = FeedJson.Deserialize(FeedFixtures.StoredFeedJson());
		feed.InstallComponents[0].Options.RemoveAt(1);
		feed.InstallComponents.Add(new InstallComponentModel
		{
			Id = "full-graph-pack",
			Kind = kind,
			ModName = kind == InstallComponentKind.Mod ? "MD's Graphics Full" : null,
			Options = kind == InstallComponentKind.ModChoice ? [new ModChoiceOptionModel { ModName = "MD's Graphics Full" }] : [],
			Title = new() { ["ru"] = "Полный графпак" },
			Description = new() { ["ru"] = "Установить полный графпак." },
			// Version ranges are ignored: the components conflict even when they never apply together.
			SupportedModpackVersion = "[1.0.0, 2.0.0)",
			Order = 2,
		});

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result, Is.EqualTo(new[]
		{
			new FeedValidationError("Компоненты",
				"Моды эксклюзивной категории «graph-packs» предлагаются в нескольких компонентах: «graph-pack», «full-graph-pack». "
				+ "Из категории можно установить только один мод."),
		}));
	}

	[Test]
	public void Validate_ModsOfDifferentCategoriesWithSameName_ReportsDuplicate()
	{
		// Arrange
		var feed = FeedJson.Deserialize(FeedFixtures.StoredFeedJson());
		var optionalMod = feed.Mods.First(x => x.CategoryId != feed.Mods[0].CategoryId);
		var renamed = optionalMod.Name;
		optionalMod.Name = feed.Mods[0].Name.ToLowerInvariant();
		foreach (var component in feed.InstallComponents.Where(x => x.ModName == renamed))
		{
			component.ModName = optionalMod.Name;
		}

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result, Is.EqualTo(new[]
		{
			new FeedValidationError("Моды", "Имя мода «MD's Graphics Lite» используется больше одного раза."),
		}));
	}

	[Test]
	public void Validate_ModOfMissingCategoryAndDuplicateCategory_ReportsBoth()
	{
		// Arrange
		var feed = FeedJson.Deserialize(FeedFixtures.StoredFeedJson());
		feed.Mods[0].CategoryId = "missing";
		feed.ModCategories.Add(FeedJson.Clone(feed.ModCategories[1]));

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result.Select(x => x.Location), Is.EquivalentTo(new[]
		{
			"Мод MD's Graphics Lite › Категория",
			"Категории модов",
		}));
	}

	[Test]
	public void Validate_GraphPackArchiveWithoutPath_ReportsMissingPath()
	{
		// Arrange
		var feed = FeedJson.Deserialize(FeedFixtures.StoredFeedJson());
		feed.Mods[2].Items[0].ArchiveDownloadInfo.S3Path = " ";

		// Act
		var result = FeedValidator.Validate(feed);

		// Assert
		Assert.That(result.Select(x => x.Location),
			Is.EqualTo(new[] { "Мод Single Archive Graphics 1.0.0 › Архив мода › Путь S3" }));
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
		var graphPack = feed.Mods[2];
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

	private static SelectChoicesAction Choices(ChoiceSelectionMode mode, params ChoiceOptionModel[] options)
	{
		var page = new ChoiceStepModel { Title = new Dictionary<string, string> { ["ru"] = "Страница" }, SelectionMode = mode };
		page.Options.AddRange(options);
		return new SelectChoicesAction { Title = new Dictionary<string, string> { ["ru"] = "Выбор" }, Steps = { page } };
	}

	private static ChoiceOptionModel Option(string id) =>
		new() { Id = id, Name = new Dictionary<string, string> { ["ru"] = id } };

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
			ModCategories = { new ModCategoryModel { Id = "mods", Title = { ["ru"] = "Моды" } } },
			Mods = { new ModModel { Name = "M", CategoryId = "mods", LogoPath = "l", DescriptionPath = new Dictionary<string, string> { ["ru"] = "d" }, Items = { item } } },
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
					InitialVersion = version, Version = version, DownloadInfo = new DownloadInfoModel { S3Path = "modpack.7z" },
					IsAvailableForSponsors = true,
				},
			},
			BetaVersions = { new BetaVersionModel { Hash = new string('A', FeedRules.Sha256HexLength), VersionRange = betaRange } },
		};
		feed.SponsorRoleIds.AddRange(sponsorRoleIds);
		return feed;
	}
}
