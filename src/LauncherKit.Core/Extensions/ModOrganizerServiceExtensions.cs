using LauncherKit.Core.Services.Abstractions;
using NuGet.Versioning;

namespace LauncherKit.Core.Extensions;

public static class ModOrganizerServiceExtensions
{
	extension(IModOrganizerService modOrganizerService)
	{
		/// <summary>
		/// The version recorded in the meta.ini of the installed mod <paramref name="modName"/> (matched
		/// case-insensitively, like the mod directories), 1.0.0 when it records none or the mod is not installed.
		/// </summary>
		public NuGetVersion ReadModVersion(string modName)
		{
			var modInfo = modOrganizerService.InstalledMods
				.FirstOrDefault(x => string.Equals(x.Name, modName, StringComparison.OrdinalIgnoreCase));
			return NuGetVersion.TryParse(modInfo?.Version, out var version) ? version : new NuGetVersion(1, 0, 0);
		}
	}
}
