using LauncherKit.Core.Feed.Mods;

namespace LauncherKit.Core.Extensions;

public static class ModFeedDataExtensions
{
	extension(ModFeedData mod)
	{
		/// <summary>
		/// Returns the display name localized for the current UI culture, falling back to <see cref="ModFeedData.Name"/>.
		/// </summary>
		public string ResolveDisplayName() => mod.DisplayName?.Resolve() ?? mod.Name;
	}
}
