using LauncherKit.Infrastructure;

namespace SampleLauncher.App;

/// <summary>
/// The sample's layout: the launcher runs from a <c>Launcher</c> directory of the launchers root (the sandbox's
/// <c>current</c>). The sample has no installer, so there is never an installed launcher.
/// </summary>
public sealed class SampleLaunchersRootDirectoryManager(
	bool useCurrentDirectory,
	IFileSystemUtils fileSystemUtils,
	IRegistryManager registryManager,
	LauncherProductOptions product)
	: LaunchersRootDirectoryManager(useCurrentDirectory, fileSystemUtils, registryManager, product)
{
	public override string? TryGetInstalledLauncherDirectory() => null;

	protected override string RootFromCurrentDirectory(string currentDirectory) =>
		Path.GetDirectoryName(currentDirectory) ?? currentDirectory;
}
