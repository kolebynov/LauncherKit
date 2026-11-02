using NuGet.Versioning;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.Models;

namespace LauncherKit.Core.Services.Abstractions;

public interface IInstallAppTaskProvider
{
	/// <summary>
	/// Installs the feed version <paramref name="version"/>, or the newest version supporting the installed STB when
	/// <see langword="null"/>, followed by its patches. An installed version is uninstalled first, so this also updates
	/// the mod, unless it is the same version with some of its patches missing: then only those patches are applied.
	/// </summary>
	ValueTask<AppTask> CreateModInstallAppTask(ModFeedData modFeed, NuGetVersion? version, CancellationToken cancellationToken);

	/// <summary>
	/// Uninstalls the mod by rolling back the installed version's rollbackable steps (see
	/// <see cref="CreateModInstallAppTask"/>); a version the feed does not list only has its mod directory removed.
	/// Running it for a listed version without <see cref="ModFeedItem.SupportsUninstall"/> throws
	/// <see cref="InvalidOperationException"/>.
	/// </summary>
	AppTask CreateModUninstallAppTask(ModFeedData modFeed);

	/// <summary>
	/// Whether the mod is installed, can be updated to the newest version supporting <paramref name="modpackVersion"/>
	/// (<see langword="null"/> when STB is not installed, which offers no update), and can be uninstalled.
	/// </summary>
	ModInstallState GetModInstallState(ModFeedData modFeed, NuGetVersion? modpackVersion);
}
