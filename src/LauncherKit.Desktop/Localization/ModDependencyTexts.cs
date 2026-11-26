using Microsoft.Extensions.Localization;
using LauncherKit.Core.Extensions;
using LauncherKit.Core.ModDependencies;

namespace LauncherKit.Desktop.Localization;

/// <summary>Texts of why a dependency alternative cannot be installed, shared by choices and errors.</summary>
internal static class ModDependencyTexts
{
	public static LocalizedString Get(IStringLocalizer<DesktopStrings> strings, ModDependencyUnavailableReason reason) =>
		reason switch
		{
			ModDependencyUnavailableReason.NoSupportedVersion(var range) =>
				strings[DesktopStrings.DependencyNoSupportedVersion, range.ToString()],
			ModDependencyUnavailableReason.NewerVersionInstalled(var version) =>
				strings[DesktopStrings.DependencyNewerVersionInstalled, version],
			ModDependencyUnavailableReason.UnknownInstalledVersion(var version) =>
				strings[DesktopStrings.DependencyUnknownInstalledVersion, version],
			ModDependencyUnavailableReason.OtherModInExclusiveCategory(var mod) =>
				strings[DesktopStrings.DependencyOtherExclusiveMod, mod.ResolveDisplayName()],
			ModDependencyUnavailableReason.ConflictingVersion(var version) =>
				strings[DesktopStrings.DependencyConflictingVersion, version],
			ModDependencyUnavailableReason.Cycle => strings[DesktopStrings.DependencyCycle],
			ModDependencyUnavailableReason.UnsatisfiableDependencies => strings[DesktopStrings.DependencyUnsatisfiable],
		};
}
