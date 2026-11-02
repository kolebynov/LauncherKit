using System.Text.Json.Serialization;
using NetEscapades.EnumGenerators;

namespace LauncherKit.Core.Feed.Mods;

/// <summary>
/// Where an installed mod goes in the selected profile's mod list, as Mod Organizer shows it (bottom-up relative to
/// <c>modlist.txt</c>). Written as <c>{"$type":"UnderSeparator","Separator":"..."}</c> or
/// <c>{"$type":"NextToMod","ModName":"...","Side":"Below"}</c>; the discriminator is the case type name. A mod
/// without a placement keeps Mod Organizer's default: a new mod goes at the end of the list.
/// </summary>
[JsonPolymorphic(InferClosedTypePolymorphism = true)]
public closed record ModPlacement
{
	/// <summary>
	/// Last under the separator <paramref name="Separator"/> (its name as shown, without the <c>_separator</c> suffix
	/// of its directory), which is created when missing.
	/// </summary>
	public sealed record UnderSeparator(string Separator) : ModPlacement;

	/// <summary>
	/// Directly above or below the mod <paramref name="ModName"/>; when that mod is not in the list, the placement is
	/// skipped and the mod keeps the default placement.
	/// </summary>
	public sealed record NextToMod(string ModName, ModListSide Side) : ModPlacement;
}

/// <summary>A side of a mod in the list as Mod Organizer shows it; written as <c>"Above"</c>/<c>"Below"</c>.</summary>
[EnumExtensions]
public enum ModListSide
{
	/// <summary>Shown above the other mod: lower priority, so the other mod's files win conflicts.</summary>
	Above,

	/// <summary>Shown below the other mod: higher priority, so its files win conflicts with the other mod.</summary>
	Below,
}
