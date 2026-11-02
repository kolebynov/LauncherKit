using Microsoft.Extensions.Localization;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Localization;
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
		ModCardViewModel.InstallUnavailableReason.Updating => strings[DesktopStrings.ModUpdating],
		ModCardViewModel.InstallUnavailableReason.OtherGraphPackInstalled(var name) => strings[DesktopStrings.OtherGraphPackInstalled, name],
		ModCardViewModel.InstallUnavailableReason.ModpackTaskQueued => strings[DesktopStrings.ModpackTaskQueued],
	};
}
