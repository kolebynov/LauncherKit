using System.Text;
using NuGet.Versioning;

namespace LauncherKit.Infrastructure;

public interface ILaunchersRootDirectoryManager
{
	string Directory { get; }

	CompositeFormat InstallLogFileNameFormat { get; }

	NuGetVersion? TryGetInstalledVersion();

	void SetInstalledVersion(NuGetVersion version);
}
