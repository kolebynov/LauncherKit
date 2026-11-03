namespace LauncherKit.Infrastructure;

public interface ILaunchersRootDirectoryManager
{
	/// <summary>The launchers root holding the global database and tools.</summary>
	/// <exception cref="InvalidOperationException">The root is read from the registry and the installer has not written it.</exception>
	string Directory { get; }

	/// <summary>
	/// The directory of the installed launcher, located by the consumer's installation layout, or <c>null</c> when no
	/// launcher is installed.
	/// </summary>
	/// <exception cref="InvalidOperationException">The installation marker is invalid.</exception>
	string? TryGetInstalledLauncherDirectory();
}
