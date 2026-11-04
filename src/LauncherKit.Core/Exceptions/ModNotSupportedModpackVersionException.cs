using NuGet.Versioning;

namespace LauncherKit.Core.Exceptions;

#pragma warning disable CA1032
public sealed class ModNotSupportedModpackVersionException : Exception
{
	public string ModName { get; }

	public NuGetVersion ModpackVersion { get; }

	public override string Message => $"Mod {ModName} does not support modpack version {ModpackVersion}";

	public ModNotSupportedModpackVersionException(string modName, NuGetVersion modpackVersion)
	{
		ModName = modName;
		ModpackVersion = modpackVersion;
	}
}