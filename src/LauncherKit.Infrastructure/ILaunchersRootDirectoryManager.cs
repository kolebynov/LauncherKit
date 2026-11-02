namespace LauncherKit.Infrastructure;

public interface ILaunchersRootDirectoryManager
{
	string Directory { get; }

	/// <summary>
	/// The directory of the installed launcher: the root's subdirectory named by the version in the root's
	/// <c>.version</c> file, written by the consumer's installer, or <c>null</c> when the file is missing.
	/// </summary>
	/// <exception cref="InvalidOperationException">The file does not contain a version.</exception>
	string? TryGetInstalledLauncherDirectory();
}
