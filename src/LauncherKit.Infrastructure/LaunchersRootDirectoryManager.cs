using System.Text;
using NuGet.Versioning;

namespace LauncherKit.Infrastructure;

public sealed class LaunchersRootDirectoryManager : ILaunchersRootDirectoryManager
{
	private readonly IFileSystemUtils _fileSystemUtils;

	public string Directory { get; }

	public CompositeFormat InstallLogFileNameFormat { get; } = CompositeFormat.Parse("install_{0}.log");

	public LaunchersRootDirectoryManager(
		bool useCurrentDirectory,
		IFileSystemUtils fileSystemUtils,
		LauncherProductOptions product,
		string? currentDirectory = null)
	{
		ArgumentNullException.ThrowIfNull(product);
		product.Validate();
		_fileSystemUtils = fileSystemUtils;
		Directory = !useCurrentDirectory
			? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), product.RootDirectoryName)
			: Path.GetDirectoryName(currentDirectory ?? System.IO.Directory.GetCurrentDirectory())!;
		_fileSystemUtils.EnsureDirectoryExists(Directory);
	}

	public NuGetVersion? TryGetInstalledVersion()
	{
		var filePath = Path.Combine(Directory, ".version");
		if (!File.Exists(filePath))
		{
			return null;
		}

		var content = File.ReadAllText(filePath);
		return NuGetVersion.TryParse(content, out var version)
			? version
			: throw new InvalidOperationException(
				$"Installed version marker '{filePath}' contains invalid version '{content}'.");
	}

	public void SetInstalledVersion(NuGetVersion version)
	{
		_fileSystemUtils.EnsureDirectoryExists(Directory);
		var filePath = Path.Combine(Directory, ".version");
		File.WriteAllText(filePath, version.ToString());
	}
}