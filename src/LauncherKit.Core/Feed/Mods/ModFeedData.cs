using NuGet.Versioning;

namespace LauncherKit.Core.Feed.Mods;

/// <summary>An optional mod, and the base of a graph pack (<see cref="GraphPacks.GraphPackFeedData"/>).</summary>
public class ModFeedData
{
	public required string Name { get; init; }

	public LocalizedText? DisplayName { get; init; }

	public required string LogoPath { get; init; }

	public required LocalizedText DescriptionPath { get; init; }

	public required IReadOnlyList<ModFeedItem> Items { get; init; }

	/// <summary>The version that <paramref name="version"/> is, or that has a patch with it, if the feed lists it.</summary>
	public ModFeedItem? FindItem(NuGetVersion? version) =>
		version == null ? null : Items.FirstOrDefault(x => x.Includes(version));
}