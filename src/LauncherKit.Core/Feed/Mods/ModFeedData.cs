using NuGet.Versioning;

namespace LauncherKit.Core.Feed.Mods;

/// <summary>
/// A mod installed into Mod Organizer: its archive, then its install steps (e.g. extracting a second archive or switching
/// to the mod's profile).
/// </summary>
public sealed class ModFeedData
{
	public required string Name { get; init; }

	/// <summary>The <see cref="ModCategoryFeedData.Id"/> of the category the mod is shown in.</summary>
	public required string CategoryId { get; init; }

	public LocalizedText? DisplayName { get; init; }

	public required string LogoPath { get; init; }

	public required LocalizedText DescriptionPath { get; init; }

	/// <summary>Shown in the mod's option of the modpack installation, without a trailing period.</summary>
	public LocalizedText? OptionDescription { get; init; }

	/// <summary>
	/// Whether the mod gets no new versions: it is offered only to modpack versions its existing versions support, while
	/// a mod that is not discontinued is shown as not yet updated for other modpack versions.
	/// </summary>
	public bool IsDiscontinued { get; init; }

	/// <summary>Whether installing the mod for the first time or uninstalling it requires starting a new game.</summary>
	public bool RequiresNewGame { get; init; }

	public required IReadOnlyList<ModFeedItem> Items { get; init; }

	/// <summary>The version that <paramref name="version"/> is, or that has a patch with it, if the feed lists it.</summary>
	public ModFeedItem? FindItem(NuGetVersion? version) =>
		version == null ? null : Items.FirstOrDefault(x => x.Includes(version));

	/// <summary>The newest mod version supporting <paramref name="modpackVersion"/>, if any.</summary>
	public ModFeedItem? FindLatestSupportedItem(NuGetVersion modpackVersion) =>
		Items.Where(x => x.SupportedModpackVersion.Satisfies(modpackVersion)).MaxBy(x => x.Version);

	/// <summary>
	/// Whether the mod is shown for <paramref name="modpackVersion"/> (<see langword="null"/> when the modpack is not
	/// installed): a discontinued mod only while one of its versions supports it.
	/// </summary>
	public bool IsOfferedFor(NuGetVersion? modpackVersion) =>
		!IsDiscontinued || (modpackVersion != null && FindLatestSupportedItem(modpackVersion) != null);
}
