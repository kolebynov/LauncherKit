using NuGet.Versioning;
using LauncherKit.Core.Feed.Modpacks;

namespace LauncherKit.Core.Update;

/// <summary>
/// A main STB version offered by the feed (one <see cref="ModpackFeedItem"/>), installed together with all its patches.
/// </summary>
/// <param name="FeedItem">The feed item installed by <see cref="IUpdateProvider.GetModpackInstallInfo"/>.</param>
/// <param name="LatestVersion">The version reached after the item's patches, or the item's own version without them.</param>
/// <param name="IsInstalled">Whether the installed STB version belongs to this item: from its initial version up to
/// <paramref name="LatestVersion"/>.</param>
public sealed record AvailableModpackVersion(ModpackFeedItem FeedItem, NuGetVersion LatestVersion, bool IsInstalled);
