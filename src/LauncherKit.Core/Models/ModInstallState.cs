namespace LauncherKit.Core.Models;

/// <summary>What the launcher can do with an installed mod; <see langword="default"/> is <see cref="NotInstalled"/>.</summary>
public readonly record struct ModInstallState
{
	public static ModInstallState NotInstalled => default;

	public bool IsInstalled { get; private init; }

	/// <summary>Installed in a version listed in the feed, and the feed has a newer version supporting the installed modpack.</summary>
	public bool IsUpdateAvailable { get; private init; }

	/// <summary>
	/// The installed version can be uninstalled: its feed item is marked <c>SupportsUninstall</c>, or the feed does not list
	/// it, so only the mod directory is removed.
	/// </summary>
	public bool IsUninstallSupported { get; private init; }

	public static ModInstallState Installed(bool isUpdateAvailable, bool isUninstallSupported) => new()
	{
		IsInstalled = true,
		IsUpdateAvailable = isUpdateAvailable,
		IsUninstallSupported = isUninstallSupported,
	};
}
