using System.Reflection;
using NuGet.Versioning;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Services;

public sealed class AppVersionProvider : IAppVersionProvider
{
	private const string AppVersionKey = "AppVersion";

	public NuGetVersion Version { get; } =
		NuGetVersion.Parse(Assembly.GetEntryAssembly()!.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion);

	public NuGetVersion? LastRunVersion { get; }

	public AppVersionProvider(IGlobalKeyValueStorage globalKeyValueStorage)
	{
		LastRunVersion = NuGetVersion.TryParse(globalKeyValueStorage.Get(AppVersionKey), out var lastRunVersion)
			? lastRunVersion
			: null;
		globalKeyValueStorage.Set(AppVersionKey, Version.ToString());
	}
}