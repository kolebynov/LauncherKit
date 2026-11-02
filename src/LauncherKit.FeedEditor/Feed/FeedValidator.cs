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
				foreach (var (step, stepIndex) in patch.InstallSteps.Select((x, i) => (x, i)))
				{
					errors.InstallStep($"{patchLocation} › Шаг {stepIndex}", step, stepIndex, null);
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

		foreach (var duplicate in feed.GraphPacks.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
		{
			errors.Add("Графпаки", $"Имя графпака «{duplicate.Key}» используется больше одного раза.");
		}

		// A discontinued default only applies to the old сборки versions it supports, where the runtime picks the first
		// available default in feed order, so it may coexist with the default for current versions.
		if (feed.GraphPacks.Count(x => x is { IsDefault: true, IsDiscontinued: false }) > 1)
		{
			errors.Add("Графпаки", "Выбранным по умолчанию может быть только один графпак, не снятый с поддержки.");
		}

		foreach (var (graphPack, index) in feed.GraphPacks.Select((x, i) => (x, i)))
		{
			errors.GraphPack($"Графпак {Label(graphPack.Name, index)}", graphPack);
		}

		foreach (var duplicate in feed.Mods.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
		{
			errors.Add("Моды", $"Имя мода «{duplicate.Key}» используется больше одного раза.");
		}

		foreach (var (mod, index) in feed.Mods.Select((x, i) => (x, i)))
		{
			errors.Mod($"Мод {Label(mod.Name, index)}", mod);
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
				case InstallComponentKind.GraphPack:
					if (component.ModName is not null) errors.Add(location, "Выбор графпака не должен ссылаться на мод.");
					break;
				case InstallComponentKind.Mod:
					if (!feed.Mods.Any(x => x.Name == component.ModName)) errors.Add(location, "Указанный мод отсутствует в фиде.");
					break;
				default:
					errors.Add(location, "Неизвестный тип компонента.");
					break;
			}
		}

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

	public static IReadOnlyList<FeedValidationError> ValidateLegacy(LegacyFeedModel feed)
	{
		var errors = new Errors(string.Empty);
		errors.UniqueVersions("Лаунчеры", feed.Launchers.Select(x => x.Version));
		foreach (var launcher in feed.Launchers)
		{
			var location = $"Лаунчер {launcher.Version}";
			errors.Check(location, "Версия", FeedRules.Version, launcher.Version);
			errors.DownloadInfo(location, launcher.DownloadInfo);
		}
		return errors;
	}

	private static string Label(string name, int index) => string.IsNullOrWhiteSpace(name) ? $"#{index + 1}" : name;

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

		public void GraphPack(string location, GraphPackModel graphPack)
		{
			Mod(location, graphPack);
			Localized(location, "Рекомендация", graphPack.Recommendation);
		}

		public void Mod(string location, ModModel mod)
		{
			Check(location, "Имя", FeedRules.Required, mod.Name);
			Check(location, "Путь к логотипу", FeedRules.Required, mod.LogoPath);
			Localized(location, "Путь к описанию", mod.DescriptionPath);
			Localized(location, "Отображаемое имя", mod.DisplayName, optional: true);

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
				foreach (var (step, stepIndex) in item.InstallSteps.Select((x, i) => (x, i)))
				{
					InstallStep($"{itemLocation} › Шаг {stepIndex}", step, stepIndex, mod.Name);
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
					foreach (var (step, stepIndex) in patch.InstallSteps.Select((x, i) => (x, i)))
					{
						InstallStep($"{patchLocation} › Шаг {stepIndex}", step, stepIndex, mod.Name);
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
		/// Checks the step at <paramref name="stepIndex"/> of a step list installed for the mod <paramref name="modName"/>,
		/// or for an сборки patch when <see langword="null"/>.
		/// </summary>
		public void InstallStep(string location, ModInstallStepModel step, int stepIndex, string? modName)
		{
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

				Check($"{location} › Условие", "Ключ", FeedRules.Required, condition.Key);
				Check($"{location} › Условие", "Значение", FeedRules.Required, condition.Value);
			}

			foreach (var (action, actionIndex) in step.Actions.Select((x, i) => (x, i)))
			{
				InstallAction($"{location} › Действие {actionIndex + 1}", action, modName);
			}
		}

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
	}
}
