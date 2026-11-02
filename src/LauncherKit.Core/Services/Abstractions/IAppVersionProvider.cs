using NuGet.Versioning;

namespace LauncherKit.Core.Services.Abstractions;

public interface IAppVersionProvider
{
	NuGetVersion Version { get; }

	/// <summary>Version that ran before this process, or null for a fresh installation.</summary>
	NuGetVersion? LastRunVersion { get; }
}
