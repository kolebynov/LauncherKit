using NuGet.Versioning;
using LauncherKit.Core.Feed.Mods;

namespace LauncherKit.Core.Feed.GraphPacks;

/// <summary>
/// A graph pack: a mod installed like an optional mod (its archive, then its install steps, e.g. extracting a second
/// archive or switching to the graph pack's profile), plus what the modpack installation needs to offer it.
/// </summary>
public sealed class GraphPackFeedData : ModFeedData
{
	/// <summary>Shown in the graph pack's option of the modpack installation, without a trailing period.</summary>
	public required LocalizedText Recommendation { get; init; }

	/// <summary>Whether the graph pack is selected by default when installing the modpack.</summary>
	public bool IsDefault { get; init; }

	/// <summary>
	/// Whether the graph pack gets no new versions: it is offered only to modpack versions its existing versions support,
	/// while a graph pack that is not discontinued is shown as being updated for other modpack versions.
	/// </summary>
	public bool IsDiscontinued { get; init; }

	/// <summary>The newest graph pack version supporting <paramref name="modpackVersion"/>, if any.</summary>
	public ModFeedItem? FindLatestSupportedItem(NuGetVersion modpackVersion) =>
		Items.Where(x => x.SupportedModpackVersion.Satisfies(modpackVersion)).MaxBy(x => x.Version);

	/// <summary>
	/// Whether the graph pack is shown for <paramref name="modpackVersion"/> (<see langword="null"/> when the modpack is not
	/// installed): a discontinued graph pack only while one of its versions supports it.
	/// </summary>
	public bool IsOfferedFor(NuGetVersion? modpackVersion) =>
		!IsDiscontinued || (modpackVersion != null && FindLatestSupportedItem(modpackVersion) != null);
}
