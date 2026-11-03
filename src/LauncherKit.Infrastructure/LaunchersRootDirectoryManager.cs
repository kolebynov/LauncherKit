namespace LauncherKit.Infrastructure;

/// <summary>
/// Resolves the launchers root. The launcher itself derives it from its working directory through the consumer's
/// <see cref="RootFromCurrentDirectory"/>, so development and sandbox runs never use an installed launcher's root;
/// hosts outside the launcher (the log collector) read it from the <see cref="RootRegistryValueName"/> value that the
/// consumer's installer writes under <see cref="LauncherProductOptions.RegistryKey"/>. The root is resolved and created
/// on first access.
/// </summary>
public abstract class LaunchersRootDirectoryManager : ILaunchersRootDirectoryManager
{
	/// <summary>The registry value, under the product's registry key, holding the installed launchers root.</summary>
	public const string RootRegistryValueName = "LaunchersRoot";

	private readonly bool _useCurrentDirectory;
	private readonly IFileSystemUtils _fileSystemUtils;
	private readonly IRegistryManager _registryManager;
	private readonly string _registryKey;
	private string? _directory;

	protected LaunchersRootDirectoryManager(
		bool useCurrentDirectory,
		IFileSystemUtils fileSystemUtils,
		IRegistryManager registryManager,
		LauncherProductOptions product)
	{
		ArgumentNullException.ThrowIfNull(product);
		product.Validate();
		_useCurrentDirectory = useCurrentDirectory;
		_fileSystemUtils = fileSystemUtils;
		_registryManager = registryManager;
		_registryKey = product.RegistryKey;
	}

	/// <exception cref="InvalidOperationException">
	/// The root is read from the registry and the installer has not written it.
	/// </exception>
	public string Directory => _directory ??= ResolveDirectory();

	public abstract string? TryGetInstalledLauncherDirectory();

	/// <summary>The launchers root of a launcher running from <paramref name="currentDirectory"/>.</summary>
	protected abstract string RootFromCurrentDirectory(string currentDirectory);

	private string ResolveDirectory()
	{
		var directory = _useCurrentDirectory
			? RootFromCurrentDirectory(_fileSystemUtils.GetCurrentDirectory())
			: ReadRootFromRegistry();
		_fileSystemUtils.EnsureDirectoryExists(directory);
		return directory;
	}

	private string ReadRootFromRegistry() =>
		_registryManager.GetValue(_registryKey, RootRegistryValueName) is string { Length: > 0 } directory
			? directory
			: throw new InvalidOperationException(
				$"Registry value '{RootRegistryValueName}' under '{_registryKey}' does not name the launchers root.");
}
