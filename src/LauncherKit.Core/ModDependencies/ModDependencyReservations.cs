using LauncherKit.Core.Feed.Mods;

namespace LauncherKit.Core.ModDependencies;

/// <summary>
/// How a mod installation's dependency resolution sees and joins the requester's tracked installations:
/// <paramref name="GetQueuedInstalls"/> returns the names of the other mods queued for installation (which run after it,
/// so a plan must not put a mod of their exclusive category before them); <paramref name="TryReserve"/> atomically
/// tracks the planned dependencies (given with the feed's categories) as queued until the installation is no longer
/// queued, unless one of them is in an exclusive category with another mod queued for install meanwhile, which it returns
/// without tracking anything (a request that ended reserves nothing); <paramref name="RequestEnded"/> is cancelled once
/// the installation request ended (its task completed, failed and was cancelled, or was removed from the queue), which
/// ends a resolution still running for it.
/// </summary>
public sealed record ModDependencyReservations(
	Func<IReadOnlyCollection<string>> GetQueuedInstalls,
	Func<IReadOnlyList<ModFeedData>, IReadOnlyList<ModCategoryFeedData>, ModReservationConflict?> TryReserve,
	CancellationToken RequestEnded);

/// <summary>The planned <paramref name="Mod"/> shares an exclusive category with the queued <paramref name="QueuedMod"/>.</summary>
public sealed record ModReservationConflict(ModFeedData Mod, ModFeedData QueuedMod);
