using System.Text.Json.Serialization;

namespace LauncherKit.Core.Feed.Install;

/// <summary>
/// A mod install action, written as <c>{"$type":"CaseName", ...case properties...}</c>. The discriminator is the case
/// type name, so renaming or removing a case is a breaking feed change.
/// </summary>
[JsonPolymorphic(InferClosedTypePolymorphism = true)]
public closed record InstallAction
{
	/// <summary>
	/// Downloads the archive and installs it as the mod <paramref name="ModName"/>, other than the mod whose version this
	/// is (its own archive is <see cref="Mods.ModFeedItem.ArchiveDownloadInfo"/>), at <paramref name="Placement"/> in the
	/// mod list when set. Rolled back by removing that mod.
	/// </summary>
	public sealed record InstallMod(DownloadInfo DownloadInfo, string ModName, Mods.ModPlacement? Placement = null)
		: InstallAction;

	public sealed record SelectRequiredMod(IReadOnlyList<string> Mods) : InstallAction;

	public sealed record ActivateMods(bool Activate, IReadOnlyList<string> Mods) : InstallAction;

	public sealed record ActivatePlugins(bool Activate, IReadOnlyList<string> Plugins) : InstallAction;

	/// <summary>
	/// Deletes the files and directories <paramref name="Paths"/>, relative to the Skyrim directory, in order; a missing
	/// one is skipped. With <paramref name="FailOnFailedDelete"/> (optional, <see langword="true"/> when missing) it
	/// fails on the first one that cannot be deleted; otherwise such a path is skipped and the rest are still deleted.
	/// </summary>
	public sealed record RemoveFileSystemItem(IReadOnlyList<string> Paths, bool FailOnFailedDelete = true) : InstallAction;

	/// <summary>
	/// Downloads the archive and extracts it whole into <paramref name="DestinationPath"/>, relative to the Skyrim
	/// directory (empty for the Skyrim directory itself).
	/// </summary>
	public sealed record ExtractArchive(DownloadInfo DownloadInfo, string DestinationPath) : InstallAction;

	/// <summary>
	/// Downloads a <c>plugins.txt</c> and merges it into the selected profile's <c>plugins.txt</c>: its plugins get its
	/// relative order and active state, plugins it does not list keep their places, and <c>loadorder.txt</c> is rebuilt
	/// from the result. Not rolled back.
	/// </summary>
	public sealed record MergePlugins(DownloadInfo DownloadInfo) : InstallAction;

	public sealed record ChangeProfile(string ProfileName) : InstallAction;
}
