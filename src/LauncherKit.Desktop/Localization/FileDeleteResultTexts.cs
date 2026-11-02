using Microsoft.Extensions.Localization;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Localization;
using LauncherKit.Core.FileStorage;

namespace LauncherKit.Desktop.Localization;

/// <summary>Default kit wording for typed file-storage deletion outcomes.</summary>
public sealed class FileDeleteResultTexts(IStringLocalizer<DesktopStrings> strings)
{
	public string Format(string fileName, IFileStorageService.DeleteResult result) => result switch
	{
		IFileStorageService.DeleteResult.Deleted => string.Empty,
		IFileStorageService.DeleteResult.FileInUse => strings[DesktopStrings.DeleteFileInUse, fileName],
		IFileStorageService.DeleteResult.AccessDenied(var path) => strings[DesktopStrings.DeleteAccessDenied, fileName, path],
		IFileStorageService.DeleteResult.Error(_, var exception) => strings[DesktopStrings.DeleteError, fileName, exception.Message],
	};
}
