using NuGet.Versioning;

namespace LauncherKit.FeedEditor.Feed;

public sealed record FeedValidationError(string Location, string Message);

/// <summary>
/// Checks the whole feed before it is saved, including entries whose editors are collapsed. A feed without errors
/// deserializes into the launcher's <c>FeedData</c> and does not contain ambiguous duplicates.
/// </summary>
public static class FeedValidator
{
	public static IReadOnlyList<FeedValidationError> Validate(FeedModel feed)
	{
		var errors = new Errors(feed.DefaultLanguage);
		errors.Check("Фид", "Язык по умолчанию", FeedRules.Language, feed.DefaultLanguage);
		errors.DownloadInfo("Загрузка Skyrim", feed.SkyrimDownloadInfo);
		errors.DownloadInfo("Загрузка 7-Zip", feed.SevenZipDownloadInfo);

		errors.UniqueVersions("Сборки", feed.Modpacks.Select(x => x.Version));
		foreach (var (modpack, index) in feed.Modpacks.Select((x, i) => (x, i)))
		{
			var location = $"Сборка {Label(modpack.Version, index)}";
			errors.Check(location, "Начальная версия", FeedRules.Version, modpack.InitialVersion);
			errors.Check(location, "Версия", FeedRules.Version, modpack.Version);
			errors.DownloadInfo(location, modpack.DownloadInfo);
			errors.UniqueVersions($"{location} › Патчи", modpack.Patches.Select(x => x.Version));
			foreach (var (patch, patchIndex) in modpack.Patches.Select((x, i) => (x, i)))
			{
				var patchLocation = $"{location} › Патч {Label(patch.Version, patchIndex)}";
				errors.Check(patchLocation, "Версия", FeedRules.Version, patch.Version);
				errors.DownloadInfo(patchLocation, patch.DownloadInfo);
				foreach (var stepIndex in Enumerable.Range(0, patch.InstallSteps.Count))
				{
					errors.InstallStep($"{patchLocation} › Шаг {stepIndex}", patch.InstallSteps, stepIndex, null);
				}
			}
		}

		errors.UniqueVersions("Списки изменений сборки", feed.ModpackChangelogs.Select(x => x.Version));
		foreach (var (changelog, index) in feed.ModpackChangelogs.Select((x, i) => (x, i)))
		{
			var location = $"Список изменений сборки {Label(changelog.Version, index)}";
			errors.Check(location, "Версия", FeedRules.Version, changelog.Version);
			errors.Localized(location, "Путь", changelog.Path);
		}

		errors.UniqueVersions("Лаунчеры", feed.Launchers.Select(x => x.Version));
		foreach (var (launcher, index) in feed.Launchers.Select((x, i) => (x, i)))
		{
			var location = $"Лаунчер {Label(launcher.Version, index)}";
			errors.Check(location, "Версия", FeedRules.Version, launcher.Version);
			errors.DownloadInfo(location, launcher.DownloadInfo);
			errors.Localized(location, "Список изменений", launcher.ChangelogPath, optional: true);
		}

		errors.UniqueIds("Категории модов", feed.ModCategories.Select(x => x.Id));
		foreach (var (category, index) in feed.ModCategories.Select((x, i) => (x, i)))
		{
			var location = $"Категория {Label(category.Id, index)}";
			errors.Check(location, "Идентификатор", FeedRules.Required, category.Id);
			errors.Localized(location, "Заголовок", category.Title);
			errors.Localized(location, "Описание", category.Description, optional: true);
		}

		// Mods are identified by their Mod Organizer name across every category.
		foreach (var duplicate in feed.Mods.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
		{
			errors.Add("Моды", $"Имя мода «{duplicate.Key}» используется больше одного раза.");
		}

		foreach (var (mod, index) in feed.Mods.Select((x, i) => (x, i)))
		{
			var location = $"Мод {Label(mod.Name, index)}";
			errors.Mod(location, mod);
			if (!feed.ModCategories.Any(x => x.Id == mod.CategoryId))
			{
				errors.Add($"{location} › Категория", "Указанная категория отсутствует в фиде.");
			}
		}

		errors.UniqueIds("Компоненты", feed.InstallComponents.Select(x => x.Id));
		foreach (var component in feed.InstallComponents)
		{
			var location = $"Компонент {component.Id}";
			errors.Check(location, "Идентификатор", FeedRules.Required, component.Id);
			errors.Check(location, "Поддерживаемые версии сборки", FeedRules.VersionRange, component.SupportedModpackVersion);
			errors.Localized(location, "Заголовок", component.Title);
			errors.Localized(location, "Описание", component.Description);
			switch (component.Kind)
			{
				case InstallComponentKind.ModChoice:
					if (component.ModName is not null) errors.Add(location, "Выбор мода не должен ссылаться на отдельный мод.");
					ModChoice(errors, location, component, feed);
					break;
				case InstallComponentKind.Mod:
					if (!feed.Mods.Any(x => x.Name == component.ModName)) errors.Add(location, "Указанный мод отсутствует в фиде.");
					if (component.Options.Count > 0) errors.Add(location, "Компонент мода не должен содержать вариантов.");
					break;
				default:
					errors.Add(location, "Неизвестный тип компонента.");
					break;
			}
		}

		ExclusiveCategoryComponents(errors, feed);

		errors.UniqueIds("Зависимости", feed.Prerequisites.Select(x => x.Id));
		foreach (var prerequisite in feed.Prerequisites)
		{
			var location = $"Зависимость {prerequisite.Id}";
			errors.Check(location, "Идентификатор", FeedRules.Required, prerequisite.Id);
			errors.DownloadInfo(location, prerequisite.DownloadInfo);
			if (prerequisite.Arguments is null) errors.Add(location, "Аргументы не могут быть null.");
		}

		foreach (var duplicate in feed.BetaVersions.GroupBy(x => x.Hash, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
		{
			errors.Add("Бета-коды", $"Хеш бета-кода «{duplicate.Key}» используется больше одного раза.");
		}

		foreach (var (betaVersion, index) in feed.BetaVersions.Select((x, i) => (x, i)))
		{
			var location = $"Бета-код #{index + 1}";
			errors.Check(location, "Хеш", FeedRules.Sha256, betaVersion.Hash);
			errors.Check(location, "Версии сборки", FeedRules.VersionRange, betaVersion.VersionRange);
		}

		CheckRoleIds(errors, feed.SponsorRoleIds, "Роли спонсоров", "Роль спонсора");
		CheckRoleIds(errors, feed.DeveloperRoleIds, "Роли разработчиков", "Роль разработчика");

		foreach (var (modpack, index) in feed.Modpacks.Select((x, i) => (x, i)).Where(x => x.x.IsAvailableForSponsors))
		{
			if (NuGetVersion.TryParse(modpack.Version, out var version) && !version.IsPrerelease)
			{
				errors.Add($"Сборка {Label(modpack.Version, index)} › Доступна спонсорам",
					"Доступ спонсорам задаётся только для предрелизных версий.");
			}
		}

		if (feed.SponsorRoleIds.Count == 0 && feed.Modpacks.Any(x => x.IsAvailableForSponsors))
		{
			errors.Add("Роли спонсоров", "Есть версии сборки для спонсоров, но не указана ни одна роль спонсора.");
		}

		return errors;
	}

	private static string Label(string name, int index) => string.IsNullOrWhiteSpace(name) ? $"#{index + 1}" : name;

	/// <summary>
	/// Checks that mods of an exclusive category, of which only one can be installed, are offered by a single install
	/// component, regardless of the modpack versions the components support.
	/// </summary>
	private static void ExclusiveCategoryComponents(Errors errors, FeedModel feed)
	{
		var exclusiveCategoryIds = feed.ModCategories.Where(x => x.IsExclusive).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
		var modCategoryIds = feed.Mods
			.Where(x => exclusiveCategoryIds.Contains(x.CategoryId))
			.GroupBy(x => x.Name, StringComparer.Ordinal)
			.ToDictionary(x => x.Key, x => x.First().CategoryId, StringComparer.Ordinal);
		var conflicts = feed.InstallComponents
			.SelectMany(component => ComponentModNames(component)
				.Select(name => modCategoryIds.GetValueOrDefault(name))
				.OfType<string>()
				.Distinct(StringComparer.Ordinal)
				.Select(categoryId => (CategoryId: categoryId, ComponentId: component.Id)))
			.GroupBy(x => x.CategoryId, StringComparer.Ordinal)
			.Where(x => x.Count() > 1);
		foreach (var conflict in conflicts)
		{
			errors.Add("Компоненты", $"Моды эксклюзивной категории «{conflict.Key}» предлагаются в нескольких компонентах: "
				+ $"{string.Join(", ", conflict.Select(x => $"«{x.ComponentId}»"))}. Из категории можно установить только один мод.");
		}
	}

	private static IEnumerable<string> ComponentModNames(InstallComponentModel component) => component.Kind switch
	{
		InstallComponentKind.ModChoice => component.Options.Select(x => x.ModName),
		InstallComponentKind.Mod when component.ModName is { } modName => [modName],
		_ => [],
	};

	/// <summary>Checks that a mod choice offers distinct existing mods, at most one of them by default.</summary>
	private static void ModChoice(Errors errors, string location, InstallComponentModel component, FeedModel feed)
	{
		if (component.Options.Count == 0)
		{
			errors.Add($"{location} › Варианты", "Нужен хотя бы один мод.");
		}

		foreach (var duplicate in component.Options.GroupBy(x => x.ModName, StringComparer.Ordinal).Where(x => x.Count() > 1))
		{
			errors.Add($"{location} › Варианты", $"Мод «{duplicate.Key}» предложен больше одного раза.");
		}

		foreach (var (option, index) in component.Options.Select((x, i) => (x, i)))
		{
			if (!feed.Mods.Any(x => x.Name == option.ModName))
			{
				errors.Add($"{location} › Вариант {Label(option.ModName, index)}", "Указанный мод отсутствует в фиде.");
			}
		}

		if (component.Options.Count(x => x.IsDefault) > 1)
		{
			errors.Add($"{location} › Варианты", "Выбранным по умолчанию может быть только один мод.");
		}
	}

	private static void CheckRoleIds(Errors errors, List<string> roleIds, string listLocation, string itemLocation)
	{
		foreach (var duplicate in roleIds.GroupBy(x => x, StringComparer.Ordinal).Where(x => x.Count() > 1))
		{
			errors.Add(listLocation, $"Роль «{duplicate.Key}» указана больше одного раза.");
		}

		foreach (var (roleId, index) in roleIds.Select((x, i) => (x, i)))
		{
			errors.Check($"{itemLocation} #{index + 1}", "ID", FeedRules.DiscordId, roleId);
		}
	}

	private sealed class Errors(string defaultLanguage) : List<FeedValidationError>
	{
		public void Add(string location, string message) => Add(new FeedValidationError(location, message));

		public void UniqueIds(string location, IEnumerable<string> ids)
		{
			foreach (var duplicate in ids.GroupBy(x => x, StringComparer.Ordinal).Where(x => x.Count() > 1))
				Add(location, $"Идентификатор «{duplicate.Key}» указан больше одного раза.");
		}

		public void Localized(string location, string field, IReadOnlyDictionary<string, string>? values, bool optional = false)
		{
			if (values is null && optional) return;
			if (values is null || string.IsNullOrWhiteSpace(defaultLanguage) || !values.ContainsKey(defaultLanguage))
				Add($"{location} › {field}", $"Нужен перевод для языка по умолчанию «{defaultLanguage}».");
			if (values is null) return;
			foreach (var (language, text) in values)
			{
				Check($"{location} › {field}", "Язык", FeedRules.Language, language);
				Check($"{location} › {field}", language, FeedRules.Required, text);
			}
			if (values.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Count)
				Add($"{location} › {field}", "Язык указан больше одного раза.");
		}

		public void Check(string location, string field, Func<string?, string?> rule, string? value)
		{
			if (rule(value) is { } message)
			{
				Add($"{location} › {field}", message);
			}
		}

		public void Lines(string location, string field, IReadOnlyCollection<string> lines)
		{
			if (lines.Count == 0)
			{
				Add($"{location} › {field}", "Нужна хотя бы одна запись.");
			}
		}

		public void DownloadInfo(string location, DownloadInfoModel downloadInfo)
		{
			Check(location, "Путь S3", FeedRules.Required, downloadInfo.S3Path);
			Check(location, "Хеш", FeedRules.OptionalSha256, downloadInfo.Hash);
		}

		public void UniqueVersions(string location, IEnumerable<string> versions)
		{
			var duplicates = versions
				.Select(x => NuGetVersion.TryParse(x, out var version) ? version : null)
				.OfType<NuGetVersion>()
				.GroupBy(x => x)
				.Where(x => x.Count() > 1);
			foreach (var duplicate in duplicates)
			{
				Add(location, $"Версия {duplicate.Key} указана больше одного раза.");
			}
		}

		public void Mod(string location, ModModel mod)
		{
			Check(location, "Имя", FeedRules.Required, mod.Name);
			Check(location, "Путь к логотипу", FeedRules.Required, mod.LogoPath);
			Localized(location, "Путь к описанию", mod.DescriptionPath);
			Localized(location, "Отображаемое имя", mod.DisplayName, optional: true);
			Localized(location, "Описание варианта", mod.OptionDescription, optional: true);

			// The installed version is looked up among versions and patches, so a version may appear only once in the mod:
			// duplicates among versions and among patches here, a patch with a mod version below.
			UniqueVersions(location, mod.Items.Select(x => x.Version));
			UniqueVersions($"{location} › Патчи", mod.Items.SelectMany(x => x.Patches.Select(patch => patch.Version)));
			var itemVersions = mod.Items
				.Select(x => NuGetVersion.TryParse(x.Version, out var version) ? version : null)
				.OfType<NuGetVersion>()
				.ToArray();
			foreach (var (item, index) in mod.Items.Select((x, i) => (x, i)))
			{
				var itemLocation = $"{location} {Label(item.Version, index)}";
				Check(itemLocation, "Версия", FeedRules.Version, item.Version);
				Check(itemLocation, "Поддерживаемые версии сборки", FeedRules.VersionRange, item.SupportedModpackVersion);
				DownloadInfo($"{itemLocation} › Архив мода", item.ArchiveDownloadInfo);
				Placement($"{itemLocation} › Размещение", item.Placement, mod.Name);
				foreach (var stepIndex in Enumerable.Range(0, item.InstallSteps.Count))
				{
					InstallStep($"{itemLocation} › Шаг {stepIndex}", item.InstallSteps, stepIndex, mod.Name);
				}

				var itemVersion = NuGetVersion.TryParse(item.Version, out var parsedItemVersion) ? parsedItemVersion : null;
				var nextItemVersion = itemVersion == null ? null : itemVersions.Where(x => x > itemVersion).Min();
				foreach (var (patch, patchIndex) in item.Patches.Select((x, i) => (x, i)))
				{
					var patchLocation = $"{itemLocation} › Патч {Label(patch.Version, patchIndex)}";
					Check(patchLocation, "Версия", FeedRules.Version, patch.Version);
					if (NuGetVersion.TryParse(patch.Version, out var patchVersion))
					{
						if (itemVersions.Contains(patchVersion))
						{
							Add($"{patchLocation} › Версия", $"Версия {patchVersion} уже указана как версия мода.");
						}
						else if (itemVersion != null && patchVersion < itemVersion)
						{
							Add($"{patchLocation} › Версия", "Версия патча должна быть выше версии мода.");
						}
						else if (nextItemVersion != null && patchVersion > nextItemVersion)
						{
							Add($"{patchLocation} › Версия", $"Версия патча должна быть ниже следующей версии мода {nextItemVersion}.");
						}
					}

					DownloadInfo(patchLocation, patch.DownloadInfo);
					foreach (var stepIndex in Enumerable.Range(0, patch.InstallSteps.Count))
					{
						InstallStep($"{patchLocation} › Шаг {stepIndex}", patch.InstallSteps, stepIndex, mod.Name);
					}
				}
			}
		}

		/// <summary>Checks the placement of the mod <paramref name="modName"/>, which cannot be placed next to itself.</summary>
		private void Placement(string location, ModPlacementModel? placement, string modName)
		{
			switch (placement?.Kind)
			{
				case null:
					break;
				case ModPlacementKind.UnderSeparator:
					Check(location, "Разделитель", FeedRules.Separator, placement.Separator);
					break;
				case ModPlacementKind.NextToMod:
					Check(location, "Мод", FeedRules.Required, placement.ModName);
					if (!string.IsNullOrWhiteSpace(placement.ModName)
					    && string.Equals(placement.ModName.Trim(), modName.Trim(), StringComparison.OrdinalIgnoreCase))
					{
						Add($"{location} › Мод", "Мод нельзя разместить рядом с самим собой.");
					}

					break;
				default:
					throw new ArgumentException($"Unsupported mod placement kind {placement.Kind}.", nameof(placement));
			}
		}

		/// <summary>
		/// Checks the step at <paramref name="stepIndex"/> of <paramref name="steps"/>, a step list installed for the mod
		/// <paramref name="modName"/>, or for an сборки patch when <see langword="null"/>.
		/// </summary>
		public void InstallStep(string location, IReadOnlyList<ModInstallStepModel> steps, int stepIndex, string? modName)
		{
			var step = steps[stepIndex];
			if (step.Actions.Count == 0)
			{
				Add(location, "Нужно хотя бы одно действие.");
			}

			if (step.Condition is { } condition)
			{
				if (condition.StepIndex < 0 || condition.StepIndex >= stepIndex)
				{
					Add($"{location} › Условие", $"Должно ссылаться на предыдущий шаг, а ссылается на шаг {condition.StepIndex}.");
				}
				else if (!string.IsNullOrWhiteSpace(condition.Key) && !PublishedKeys(steps[condition.StepIndex]).Contains(condition.Key))
				{
					Add($"{location} › Условие › Ключ", $"Шаг {condition.StepIndex} не задаёт ключ «{condition.Key}».");
				}
				else if (ChoiceKeys(steps[condition.StepIndex]).Contains(condition.Key)
				         && condition.Value is not (SelectChoicesAction.Selected or SelectChoicesAction.NotSelected))
				{
					Add($"{location} › Условие › Значение",
						$"Вариант выбора имеет значение «{SelectChoicesAction.Selected}» или «{SelectChoicesAction.NotSelected}».");
				}

				Check($"{location} › Условие", "Ключ", FeedRules.Required, condition.Key);
				Check($"{location} › Условие", "Значение", FeedRules.Required, condition.Value);
			}

			// Every key the step publishes is read from the same context, so it must be published once.
			foreach (var duplicate in PublishedKeys(step).GroupBy(x => x, StringComparer.Ordinal).Where(x => x.Count() > 1))
			{
				Add(location, $"Ключ «{duplicate.Key}» задаётся в шаге больше одного раза.");
			}

			foreach (var (action, actionIndex) in step.Actions.Select((x, i) => (x, i)))
			{
				InstallAction($"{location} › Действие {actionIndex + 1}", action, modName);
			}
		}

		private static IEnumerable<string> ChoiceKeys(ModInstallStepModel step) =>
			step.Actions.OfType<SelectChoicesAction>().SelectMany(x => x.Steps).SelectMany(x => x.Options).Select(x => x.Id)
				.Where(x => !string.IsNullOrWhiteSpace(x));

		/// <summary>The context keys the step's actions set, which conditions of later steps can read.</summary>
		private static IEnumerable<string> PublishedKeys(ModInstallStepModel step) =>
			step.Actions.OfType<SelectRequiredModAction>().Any()
				? ChoiceKeys(step).Append(ModInstallConditionModel.SelectedModKey)
				: ChoiceKeys(step);

		private void InstallAction(string location, ModInstallActionModel action, string? modName)
		{
			switch (action)
			{
				case InstallModAction installMod:
					DownloadInfo(location, installMod.DownloadInfo);
					Check(location, "Имя мода", FeedRules.Required, installMod.ModName);
					if (modName != null && !string.IsNullOrWhiteSpace(installMod.ModName)
					    && string.Equals(installMod.ModName.Trim(), modName.Trim(), StringComparison.OrdinalIgnoreCase))
					{
						Add($"{location} › Имя мода", "Основной архив мода устанавливается автоматически; это действие — для установки других модов.");
					}

					Placement($"{location} › Размещение", installMod.Placement, installMod.ModName);
					break;
				case SelectRequiredModAction selectRequiredMod:
					Lines(location, "Моды", selectRequiredMod.Mods);
					break;
				case SelectChoicesAction selectChoices:
					SelectChoices(location, selectChoices);
					break;
				case ActivateModsAction activateMods:
					Lines(location, "Моды", activateMods.Mods);
					break;
				case ActivatePluginsAction activatePlugins:
					Lines(location, "Плагины", activatePlugins.Plugins);
					break;
				case RemoveFileSystemItemAction removeFileSystemItem:
					Lines(location, "Пути", removeFileSystemItem.Paths);
					break;
				case ExtractArchiveAction extractArchive:
					DownloadInfo(location, extractArchive.DownloadInfo);
					// An empty destination is the Skyrim directory itself.
					break;
				case MergePluginsAction mergePlugins:
					DownloadInfo(location, mergePlugins.DownloadInfo);
					break;
				case ChangeProfileAction changeProfile:
					Check(location, "Имя профиля", FeedRules.Required, changeProfile.ProfileName);
					break;
				default:
					throw new ArgumentException($"Unsupported mod install action type {action.GetType()}.", nameof(action));
			}
		}

		private void SelectChoices(string location, SelectChoicesAction action)
		{
			Localized(location, "Заголовок окна", action.Title);
			if (action.Steps.Count == 0)
			{
				Add($"{location} › Страницы", "Нужна хотя бы одна страница.");
			}

			foreach (var (step, stepIndex) in action.Steps.Select((x, i) => (x, i)))
			{
				var stepLocation = $"{location} › Страница {stepIndex + 1}";
				Localized(stepLocation, "Заголовок", step.Title);
				Localized(stepLocation, "Подзаголовок", step.Subtitle, optional: true);
				if (step.Options.Count == 0)
				{
					Add($"{stepLocation} › Варианты", "Нужен хотя бы один вариант.");
				}

				// One of these modes cannot be satisfied when two options are always selected.
				if (step.SelectionMode is ChoiceSelectionMode.ExactlyOne or ChoiceSelectionMode.AtMostOne
				    && step.Options.Count(x => x.IsRequired) > 1)
				{
					Add($"{stepLocation} › Варианты", "При выборе не больше одного варианта обязательным может быть только один.");
				}

				foreach (var (option, optionIndex) in step.Options.Select((x, i) => (x, i)))
				{
					var optionLocation = $"{stepLocation} › Вариант {Label(option.Id, optionIndex)}";
					Check(optionLocation, "Идентификатор", FeedRules.Required, option.Id);
					Localized(optionLocation, "Название", option.Name);
					Localized(optionLocation, "Описание", option.Description, optional: true);
				}
			}
		}
	}
}
