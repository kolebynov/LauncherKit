using System.Diagnostics;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.Utils;

public sealed class SkyrimInstallationChecker : ISkyrimInstallationChecker
{
	public SkyrimDirectoryCheckResult CheckSkyrimDirectory(string? directory)
	{
		if (string.IsNullOrEmpty(directory))
		{
			return SkyrimDirectoryCheckResult.NotSkyrimDirectory.Instance;
		}

		var skyrimSePath = Path.Combine(directory, "SkyrimSE.exe");
		if (!File.Exists(skyrimSePath))
		{
			return SkyrimDirectoryCheckResult.NotSkyrimDirectory.Instance;
		}

		var versionInfo = FileVersionInfo.GetVersionInfo(skyrimSePath);

		// The product version resource string is authoritative: Skyrim's numeric product fields do not
		// match the shipped build. A malformed or suffixed string ("1.5.97.0 beta") falls back to those
		// numeric fields instead of throwing, so directory selection never aborts on an odd executable.
		var version = Version.TryParse(versionInfo.ProductVersion, out var productVersion)
			? productVersion
			: new Version(
				versionInfo.ProductMajorPart,
				versionInfo.ProductMinorPart,
				versionInfo.ProductBuildPart,
				versionInfo.ProductPrivatePart);

		return new SkyrimDirectoryCheckResult.SkyrimDirectory(version);
	}
}
