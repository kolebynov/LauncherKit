using Microsoft.Extensions.Localization;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Localization;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.UserSettings;

namespace LauncherKit.Desktop.Localization;

public sealed class EnumTexts(IStringLocalizer<DesktopStrings> strings)
{
	public LocalizedString Get(FileStoragePeriod period) => period switch
	{
		FileStoragePeriod.Day => strings[DesktopStrings.StorageDay],
		FileStoragePeriod.Week => strings[DesktopStrings.StorageWeek],
		FileStoragePeriod.Month => strings[DesktopStrings.StorageMonth],
		FileStoragePeriod.Unlimited => strings[DesktopStrings.StorageUnlimited],
		_ => throw new ArgumentOutOfRangeException(nameof(period), period, null),
	};

	public LocalizedString Get(StoredFileStatus status) => status switch
	{
		StoredFileStatus.Complete => strings[DesktopStrings.StoredComplete],
		StoredFileStatus.Partial => strings[DesktopStrings.StoredPartial],
		_ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
	};
}
