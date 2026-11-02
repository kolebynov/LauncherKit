using NuGet.Versioning;

namespace LauncherKit.Infrastructure;

public sealed class LaunchersRootDirectoryManager : ILaunchersRootDirectoryManager
{
	public string Directory { get; }

	public LaunchersRootDirectoryManager(
		bool useCurrentDirectory,
		IFileSystemUtils fileSystemUtils,
		LauncherProductOptions product,
		string? currentDirectory = null)
	{
		ArgumentNullException.ThrowIfNull(product);
		product.Validate();
		Directory = !useCurrentDirectory
			? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), product.RootDirectoryName)
			: Path.GetDirectoryName(currentDirectory ?? System.IO.Directory.GetCurrentDirectory())!;
		fileSystemUtils.EnsureDirectoryExists(Directory);
	}

	public string? TryGetInstalledLauncherDirectory()
	{
		var filePath = Path.Combine(Directory, ".version");
		if (!File.Exists(filePath))
		{
			return null;
		}

		var content = File.ReadAllText(filePath);
		// Parsed rather than used as is, so the file cannot name a path outside the root.
		return NuGetVersion.TryParse(content, out var version)
			? Path.Combine(Directory, version.ToString())
			: throw new InvalidOperationException(
				$"Installed version marker '{filePath}' contains invalid version '{content}'.");
	}
}
