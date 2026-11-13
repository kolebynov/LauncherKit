namespace LauncherKit.Core.Feed.Mods;

/// <summary>A feed-defined group of mods, shown in feed order as a section of mod cards.</summary>
public sealed class ModCategoryFeedData
{
	/// <summary>Stable identifier that <see cref="ModFeedData.CategoryId"/> references.</summary>
	public required string Id { get; init; }

	public required LocalizedText Title { get; init; }

	/// <summary>Markdown shown at the top of the section, if any.</summary>
	public LocalizedText? DescriptionPath { get; init; }

	/// <summary>
	/// Whether only one mod of the category can be installed at a time: while one is installed or being installed, the
	/// others cannot be installed. The installed mod is never replaced automatically.
	/// </summary>
	public bool IsExclusive { get; init; }
}
