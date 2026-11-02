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

		var ids = new HashSet<string>(StringComparer.Ordinal);
		foreach (var component in feed.InstallComponents)
		{
			if (string.IsNullOrWhiteSpace(component.Id) || !ids.Add(component.Id))
			{
				throw new InvalidDataException("Component IDs must be nonempty and unique.");
			}

			if (component.Kind == InstallComponentKind.Mod &&
				!((feed.Mods ?? []).Any(mod => mod.Name == component.ModName)))
			{
				throw new InvalidDataException($"Component '{component.Id}' must reference an existing mod.");
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

		foreach (var mod in (feed.Mods ?? []).Concat(feed.GraphPacks ?? []))
		{
			if (mod.DisplayName is { } displayName)
			{
				yield return displayName;
			}

			yield return mod.DescriptionPath;
		}

		foreach (var graphPack in feed.GraphPacks ?? [])
		{
			yield return graphPack.Recommendation;
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
	}
}
