using NuGet.Versioning;
using LauncherKit.Core.Backups;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.Models;

namespace LauncherKit.Core.Services.Abstractions;

public interface IModOrganizerService
{
	string ModsDirectory { get; }

	IEnumerable<ModInfo> InstalledMods { get; }

	/// <summary>
	/// Extracts the archive into the mod's directory and activates the mod and its plugins in the selected profile,
	/// at <paramref name="placement"/> when set (see <see cref="PlaceMod"/>).
	/// </summary>
	Task InstallMod(string modName, string archivePath, ModPlacement? placement, CancellationToken cancellationToken);

	/// <summary>
	/// Moves the mod in the selected profile's mod list, keeping its activation state (a mod not listed yet is added
	/// deactivated). <see cref="ModPlacement.UnderSeparator"/> puts it last under the separator; a missing separator is
	/// created with its directory and shown right above the "ВАШИ МОДЫ" separator, or at the end of the mod list without
	/// it. <see cref="ModPlacement.NextToMod"/> puts it directly above or below that mod, and leaves the list unchanged
	/// when that mod is not listed.
	/// </summary>
	Task PlaceMod(string modName, ModPlacement placement, CancellationToken cancellationToken);

	Task UninstallMod(string modName, CancellationToken cancellationToken);

	Task ChangeProfile(string newProfile, CancellationToken cancellationToken);

	Task ActivateMod(string modName, bool activate, bool activatePlugins, CancellationToken cancellationToken);

	bool IsModInstalled(string modName);

	Task<IEnumerable<ModListItem>> GetModList(CancellationToken cancellationToken);

	Task ActivatePlugins(IReadOnlyList<string> pluginNames, bool activate, CancellationToken cancellationToken);

	/// <summary>
	/// Merges the <c>plugins.txt</c> at <paramref name="pluginsFilePath"/>, which may list only some plugins, into the
	/// selected profile's <c>plugins.txt</c> (its plugins get its order and active state, see
	/// <see cref="PluginListMerger.MergePlugins"/>), then rebuilds the profile's <c>loadorder.txt</c> from the result.
	/// With a <paramref name="backup"/>, the profile and both lists are kept there first, so
	/// <see cref="RevertPluginsMerge"/> can undo the merge.
	/// </summary>
	Task MergePlugins(string pluginsFilePath, InstallActionBackup? backup, CancellationToken cancellationToken);

	/// <summary>
	/// Undoes the merge kept in <paramref name="backup"/> in its profile's <c>plugins.txt</c>, keeping what changed since
	/// (see <see cref="PluginListMerger.RevertMerge"/>), rebuilds that profile's <c>loadorder.txt</c>, and deletes the
	/// backup. A profile without <c>plugins.txt</c> is left as is.
	/// </summary>
	/// <returns><see langword="false"/> when there is no such backup.</returns>
	Task<bool> RevertPluginsMerge(InstallActionBackup backup, CancellationToken cancellationToken);

	/// <summary>
	/// Writes <paramref name="version"/> to the installed mod's meta.ini, and <paramref name="installationFile"/> when it
	/// is not <see langword="null"/> (otherwise the recorded archive is kept).
	/// </summary>
	Task CreateOrUpdateMetaIniFile(string modName, string? installationFile, NuGetVersion version, string? comments,
		CancellationToken cancellationToken);

	Task StartModOrganizer(SkyrimRelativePath? programPath, string? programArguments, bool assertSuccess,
		CancellationToken cancellationToken);

	Task OneShotStartModOrganizer(CancellationToken cancellationToken);

	/// <summary>
	/// Deletes every save of the selected profile except the <paramref name="savesToKeep"/> most recently written ones.
	/// A save is the game's <c>.ess</c> file plus the SKSE co-save <c>.skse</c> with the same name. Co-saves without
	/// a game file are deleted too when they were written before the oldest kept save (the older of its two files);
	/// with <paramref name="savesToKeep"/> 0 all of them are deleted.
	/// </summary>
	/// <returns>
	/// The number of deleted saves and the size of their deleted files. A failure or cancellation while deleting
	/// does not throw but is returned in <see cref="DeletedSavesInfo.Error"/> with what was deleted before it.
	/// </returns>
	Task<DeletedSavesInfo> DeleteOldSaves(int savesToKeep, CancellationToken cancellationToken);
}
