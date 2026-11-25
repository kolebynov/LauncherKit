using System.Text.Json.Serialization;
using NuGet.Versioning;

namespace LauncherKit.Core.Feed.Mods;

/// <summary>
/// A requirement of a mod version (<see cref="ModFeedItem.Dependencies"/>), resolved when the mod is installed or updated
/// from its card. Written as <c>{"$type":"AnotherMod","Mods":[...]}</c>; the discriminator is the case type name.
/// </summary>
[JsonPolymorphic(InferClosedTypePolymorphism = true)]
public closed record ModDependency
{
	/// <summary>
	/// Any one of <paramref name="Mods"/> (in preference order, never empty) installed with a version in its range.
	/// </summary>
	public sealed record AnotherMod(IReadOnlyList<ModDependencyAlternative> Mods) : ModDependency;
}

/// <summary>
/// The feed mod <paramref name="ModName"/> with a version (a version of the mod or one of its patches) in
/// <paramref name="VersionRange"/>.
/// </summary>
public sealed record ModDependencyAlternative(string ModName, VersionRange VersionRange);
