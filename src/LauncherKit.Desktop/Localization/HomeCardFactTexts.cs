using Microsoft.Extensions.Localization;
using LauncherKit.Desktop.ViewModels;

namespace LauncherKit.Desktop.Localization;

public sealed class HomeCardFactTexts(IStringLocalizer<DesktopStrings> strings)
{
	public LocalizedString Get(ModpackInstallUnavailableReason reason) => reason switch
	{
		ModpackInstallUnavailableReason.NoAvailableVersions => strings[DesktopStrings.ModpackInstallUnavailable],
		_ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
	};

	public LocalizedString Get(ModCardViewModel.InstallUnavailableReason reason) => reason switch
	{
		ModCardViewModel.InstallUnavailableReason.NotUpdated => strings[DesktopStrings.ModNotUpdated],
		ModCardViewModel.InstallUnavailableReason.OtherModInstalled(var name) => strings[DesktopStrings.OtherModInstalled, name],
		ModCardViewModel.InstallUnavailableReason.ModpackTaskQueued => strings[DesktopStrings.ModpackTaskQueued],
	};
}
