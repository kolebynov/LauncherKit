using NuGet.Versioning;
using R3;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.Models;
using LauncherKit.Core.Update;

namespace LauncherKit.Core.Services.Abstractions;

/// <summary>
/// Enqueues mod and modpack installations, updates and removals, and tracks those still queued: waiting, running, or failed
/// until resumed or cancelled. An installation leaves <see cref="QueuedInstallations"/> when it is no longer queued,
/// whether it succeeded, was cancelled or skipped, so finishing is no claim of success.
/// </summary>
public interface IInstallationService
{
	/// <summary>Installations still queued, in the order they were queued.</summary>
	IReadOnlyList<QueuedInstallation> QueuedInstallations { get; }

	/// <summary>
	/// Raised, possibly on the queue's processing thread, after <see cref="QueuedInstallations"/> changed and, when the
	/// modpack installation chain finished, after the installed modpack version was refreshed.
	/// </summary>
	Observable<InstallationChange> Changes { get; }

	/// <summary>
	/// The mod's installed facts against the installed modpack version (see
	/// <see cref="IInstallAppTaskProvider.GetModInstallState"/>).
	/// </summary>
	ModInstallState GetModInstallState(ModFeedData mod);

	/// <summary>
	/// Installs or updates the mod of <paramref name="category"/> (see
	/// <see cref="IInstallAppTaskProvider.CreateModInstallAppTask"/>) and waits until it is no longer queued; does nothing
	/// while an installation or removal of the same mod is queued or, when <paramref name="category"/> is exclusive, an
	/// installation of another mod of it is.
	/// </summary>
	Task InstallMod(ModFeedData mod, ModCategoryFeedData category, NuGetVersion? version,
		CancellationToken cancellationToken);

	/// <summary>Uninstalls the mod and waits until it is no longer queued; does nothing while the mod is already queued.</summary>
	Task UninstallMod(ModFeedData mod, CancellationToken cancellationToken);

	/// <summary>Installs or updates the modpack and waits until its task is no longer queued.</summary>
	Task InstallModpack(ModpackUpdateInfo info, CancellationToken cancellationToken);

	/// <summary>Uninstalls the modpack and waits until its task is no longer queued.</summary>
	Task UninstallModpack(CancellationToken cancellationToken);

	/// <summary>Runs the game, installing the modpack first when needed, and waits until it is no longer queued.</summary>
	Task RunGame(GameLaunch launch, CancellationToken cancellationToken);
}

public closed record QueuedInstallation
{
	/// <summary>
	/// A task of the modpack installation chain (component choice, modpack, patches, components) or the modpack removal,
	/// including the chain a game launch queues; it lasts until the chain's last task is no longer queued.
	/// </summary>
	public sealed record Modpack : QueuedInstallation;

	public sealed record ModInstall(ModFeedData Mod) : QueuedInstallation;

	public sealed record ModUninstall(ModFeedData Mod) : QueuedInstallation;
}

public closed record InstallationChange
{
	public sealed record Queued(QueuedInstallation Installation) : InstallationChange;

	/// <summary>The installation is no longer queued: it succeeded, was cancelled or skipped.</summary>
	public sealed record Finished(QueuedInstallation Installation) : InstallationChange;
}
