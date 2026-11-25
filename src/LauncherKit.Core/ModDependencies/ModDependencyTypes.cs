using NuGet.Versioning;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.Models;

namespace LauncherKit.Core.ModDependencies;

/// <summary>
/// An installed mod's version; not <paramref name="IsFinished"/> while the installation of <paramref name="Version"/>
/// started and did not finish, so it does not meet a dependency yet.
/// </summary>
public sealed record InstalledModVersion(NuGetVersion Version, bool IsFinished);

/// <summary>
/// Installing <paramref name="Item"/> of <paramref name="Mod"/> with its patches up to <paramref name="TargetVersion"/>,
/// replacing <paramref name="InstalledVersion"/> when the mod is installed.
/// </summary>
public sealed record PlannedModInstall(ModFeedData Mod, ModFeedItem Item, NuGetVersion TargetVersion,
	NuGetVersion? InstalledVersion);

/// <summary>
/// An alternative of a dependency: available when the plan can meet the dependency with it, either by installing it
/// (<see cref="Install"/>) or by keeping its installed <see cref="KeptVersion"/>; otherwise unavailable for
/// <see cref="UnavailableReason"/>. Exactly one of the three is set.
/// </summary>
public sealed record ModDependencyOption(ModFeedData Mod, PlannedModInstall? Install, NuGetVersion? KeptVersion,
	ModDependencyUnavailableReason? UnavailableReason)
{
	public bool IsAvailable => UnavailableReason == null;
}

/// <summary>
/// Which alternative to install for the dependency of <paramref name="Dependent"/> at <paramref name="Index"/> of its
/// version's <see cref="ModFeedItem.Dependencies"/>.
/// </summary>
public sealed record ModDependencyChoice(ModFeedData Dependent, int Index, IReadOnlyList<ModDependencyOption> Options);

/// <summary>Why an alternative of a dependency cannot be installed.</summary>
public union ModDependencyUnavailableReason(
	ModDependencyUnavailableReason.NoSupportedVersion,
	ModDependencyUnavailableReason.NewerVersionInstalled,
	ModDependencyUnavailableReason.UnknownInstalledVersion,
	ModDependencyUnavailableReason.OtherModInExclusiveCategory,
	ModDependencyUnavailableReason.ConflictingVersion,
	ModDependencyUnavailableReason.Cycle,
	ModDependencyUnavailableReason.UnsatisfiableDependencies)
{
	/// <summary>No version in <paramref name="Range"/> supports the installed modpack.</summary>
	public sealed record NoSupportedVersion(VersionRange Range);

	/// <summary>Installing a version in range would downgrade the installed <paramref name="Version"/>.</summary>
	public sealed record NewerVersionInstalled(NuGetVersion Version);

	/// <summary>The installed <paramref name="Version"/> is not in the feed, so it cannot be replaced.</summary>
	public sealed record UnknownInstalledVersion(NuGetVersion Version);

	/// <summary>The category is exclusive and <paramref name="Mod"/> of it is installed, planned or queued.</summary>
	public sealed record OtherModInExclusiveCategory(ModFeedData Mod);

	/// <summary>The mod is already planned with <paramref name="Version"/>, which is out of range.</summary>
	public sealed record ConflictingVersion(NuGetVersion Version);

	/// <summary>The mod itself waits for this dependency to be installed.</summary>
	public sealed record Cycle;

	/// <summary>
	/// A version in range could be installed, but then the requirements of the plan (its own or another mod's) cannot
	/// all be met.
	/// </summary>
	public sealed record UnsatisfiableDependencies;
}

/// <summary>An installed dependency the plan replaces with a newer version.</summary>
public sealed record ModUpgrade(ModFeedData Mod, NuGetVersion InstalledVersion, NuGetVersion TargetVersion);

/// <summary>
/// Asks whether installing <see cref="RequestedMod"/> may update the installed <see cref="Upgrades"/> and install
/// <see cref="NewGameInstalls"/>, mods installed for the first time that require a new game; completes with
/// <see langword="true"/> to continue and <see langword="false"/> to install nothing.
/// </summary>
public sealed class ModUpgradesConfirmation : BaseAsyncResult<bool>
{
	public ModFeedData RequestedMod { get; }

	public IReadOnlyList<ModUpgrade> Upgrades { get; }

	/// <summary>The dependencies installed for the first time with <see cref="ModFeedData.RequiresNewGame"/>.</summary>
	public IReadOnlyList<ModFeedData> NewGameInstalls { get; }

	public ModUpgradesConfirmation(ModFeedData requestedMod, IReadOnlyList<ModUpgrade> upgrades,
		IReadOnlyList<ModFeedData> newGameInstalls, CancellationToken cancellationToken)
		: base(cancellationToken)
	{
		RequestedMod = requestedMod;
		Upgrades = upgrades;
		NewGameInstalls = newGameInstalls;
	}
}
