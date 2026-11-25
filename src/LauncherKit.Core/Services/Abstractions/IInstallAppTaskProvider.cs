using NuGet.Versioning;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.Models;
using LauncherKit.Core.ModDependencies;

namespace LauncherKit.Core.Services.Abstractions;

public interface IInstallAppTaskProvider
{
	/// <summary>
	/// Installs the feed version <paramref name="version"/>, or the newest version supporting the installed modpack when
	/// <see langword="null"/>, followed by its patches. An installed version is uninstalled first, so this also updates
	/// the mod, unless it is the same version with some of its patches missing: then only those patches are applied.
	/// </summary>
	ValueTask<AppTask> CreateModInstallAppTask(ModFeedData modFeed, NuGetVersion? version, CancellationToken cancellationToken);

	/// <summary>
	/// Like <see cref="CreateModInstallAppTask"/>, but when the mod's versions declare
	/// <see cref="ModFeedItem.Dependencies"/>, a preceding task first plans the installations they need (asking which
	/// alternative to install and, before updating installed dependencies, whether to continue) and the task installs
	/// them in dependency order before the mod. Declining installs nothing: the task is skipped as unneeded. A dependency
	/// that cannot be met fails the planning task with <see cref="Exceptions.ModDependencyUnavailableException"/>.
	/// </summary>
	ValueTask<AppTask> CreateModInstallWithDependenciesAppTask(ModFeedData modFeed, NuGetVersion? version,
		ModDependencyReservations reservations, CancellationToken cancellationToken);

	/// <summary>
	/// Uninstalls the mod by rolling back the installed version's rollbackable steps (see
	/// <see cref="CreateModInstallAppTask"/>); a version the feed does not list only has its mod directory removed.
	/// Running it for a listed version without <see cref="ModFeedItem.SupportsUninstall"/> throws
	/// <see cref="InvalidOperationException"/>.
	/// </summary>
	AppTask CreateModUninstallAppTask(ModFeedData modFeed);

	/// <summary>
	/// Whether the mod is installed, can be updated to the newest version supporting <paramref name="modpackVersion"/>
	/// (<see langword="null"/> when the modpack is not installed, which offers no update), and can be uninstalled.
	/// </summary>
	ModInstallState GetModInstallState(ModFeedData modFeed, NuGetVersion? modpackVersion);
}
