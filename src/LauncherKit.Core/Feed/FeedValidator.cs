using LauncherKit.Core.Feed.Install;

namespace LauncherKit.Core.Feed;

public static class FeedValidator
{
	/// <summary>Validates the language contract and binds each value to this feed's fallback language.</summary>
	public static void Validate(FeedData feed)
	{
		if (string.IsNullOrWhiteSpace(feed.DefaultLanguage))
		{
			throw new InvalidDataException("DefaultLanguage must not be empty.");
		}

		foreach (var text in GetLocalizedValues(feed))
		{
			if (!text.ContainsKey(feed.DefaultLanguage))
			{
				throw new InvalidDataException($"Localized feed content must contain '{feed.DefaultLanguage}'.");
			}

			text.DefaultLanguage = feed.DefaultLanguage;
		}

		var categoryIds = new HashSet<string>(StringComparer.Ordinal);
		foreach (var category in feed.ModCategories)
		{
			if (string.IsNullOrWhiteSpace(category.Id) || !categoryIds.Add(category.Id))
			{
				throw new InvalidDataException("Mod category IDs must be nonempty and unique.");
			}
		}

		var modNames = new HashSet<string>(StringComparer.Ordinal);
		foreach (var mod in feed.Mods)
		{
			if (string.IsNullOrWhiteSpace(mod.Name) || !modNames.Add(mod.Name))
			{
				throw new InvalidDataException("Mod names must be nonempty and unique.");
			}

			if (!categoryIds.Contains(mod.CategoryId))
			{
				throw new InvalidDataException($"Mod '{mod.Name}' must reference an existing category.");
			}
		}

		var ids = new HashSet<string>(StringComparer.Ordinal);
		foreach (var component in feed.InstallComponents)
		{
			if (string.IsNullOrWhiteSpace(component.Id) || !ids.Add(component.Id))
			{
				throw new InvalidDataException("Component IDs must be nonempty and unique.");
			}

			switch (component.Kind)
			{
				case InstallComponentKind.Mod when component.ModName == null || !modNames.Contains(component.ModName):
					throw new InvalidDataException($"Component '{component.Id}' must reference an existing mod.");
				case InstallComponentKind.ModChoice:
					ValidateModChoice(component, modNames);
					break;
			}
		}

		foreach (var steps in GetInstallStepLists(feed))
		{
			foreach (var step in steps)
			{
				ValidateChoiceKeys(step);
			}
		}
	}

	/// <summary>Checks that the options reference distinct existing mods and that at most one is the default.</summary>
	private static void ValidateModChoice(InstallComponent component, HashSet<string> modNames)
	{
		var options = component.Options ?? [];
		var optionNames = new HashSet<string>(StringComparer.Ordinal);
		if (options.Count == 0 || options.Any(x => !modNames.Contains(x.ModName) || !optionNames.Add(x.ModName)))
		{
			throw new InvalidDataException($"Component '{component.Id}' must offer distinct existing mods.");
		}

		if (options.Count(x => x.IsDefault) > 1)
		{
			throw new InvalidDataException($"Component '{component.Id}' must have at most one default option.");
		}
	}

	/// <summary>
	/// Checks that every <see cref="InstallAction.SelectChoices"/> action has pages, each with options, and that the
	/// option IDs, which are the context keys conditions read, are nonempty and unique within the install step.
	/// </summary>
	private static void ValidateChoiceKeys(InstallStep step)
	{
		var ids = new HashSet<string>(StringComparer.Ordinal);
		foreach (var choices in step.Actions.OfType<InstallAction.SelectChoices>())
		{
			if (choices.Steps.Count == 0 || choices.Steps.Any(x => x.Options.Count == 0))
			{
				throw new InvalidDataException("A choice action must have pages, each with options.");
			}

			foreach (var option in choices.Steps.SelectMany(x => x.Options))
			{
				if (string.IsNullOrWhiteSpace(option.Id) || !ids.Add(option.Id))
				{
					throw new InvalidDataException("Choice option IDs must be nonempty and unique within an install step.");
				}
			}
		}
	}

	private static IEnumerable<IReadOnlyList<InstallStep>> GetInstallStepLists(FeedData feed)
	{
		foreach (var patch in feed.Modpacks.SelectMany(x => x.Patches ?? []))
		{
			yield return patch.InstallSteps ?? [];
		}

		foreach (var item in feed.Mods.SelectMany(x => x.Items))
		{
			yield return item.InstallSteps ?? [];
			foreach (var patch in item.Patches ?? [])
			{
				yield return patch.InstallSteps ?? [];
			}
		}
	}

	private static IEnumerable<LocalizedText> GetLocalizedValues(FeedData feed)
	{
		foreach (var component in feed.InstallComponents)
		{
			yield return component.Title;
			yield return component.Description;
		}

		foreach (var category in feed.ModCategories)
		{
			yield return category.Title;
			if (category.Description is { } description)
			{
				yield return description;
			}
		}

		foreach (var mod in feed.Mods)
		{
			if (mod.DisplayName is { } displayName)
			{
				yield return displayName;
			}

			yield return mod.DescriptionPath;
			if (mod.OptionDescription is { } optionDescription)
			{
				yield return optionDescription;
			}
		}

		foreach (var changelog in feed.ModpackChangelogs)
		{
			yield return changelog.Path;
		}

		foreach (var launcher in feed.Launchers)
		{
			if (launcher.ChangelogPath is { } path)
			{
				yield return path;
			}
		}

		foreach (var choices in GetInstallStepLists(feed).SelectMany(x => x).SelectMany(x => x.Actions)
			.OfType<InstallAction.SelectChoices>())
		{
			yield return choices.Title;
			foreach (var step in choices.Steps)
			{
				yield return step.Title;
				if (step.Subtitle is { } subtitle)
				{
					yield return subtitle;
				}

				foreach (var option in step.Options)
				{
					yield return option.Name;
					if (option.Description is { } description)
					{
						yield return description;
					}
				}
			}
		}
	}
}
