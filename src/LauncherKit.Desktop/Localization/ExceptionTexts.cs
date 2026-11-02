using System.Net.Sockets;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Localization;
using Microsoft.Extensions.Localization;
using LauncherKit.Core.Exceptions;
using LauncherKit.Core.Utils;
using LauncherKit.Infrastructure;

namespace LauncherKit.Desktop.Localization;

public sealed class ExceptionTexts(IStringLocalizer<DesktopStrings> strings)
{
	public LocalizedString Get(Exception exception) => exception switch
	{
		FileSystemItemDeletionException e => strings[DesktopStrings.ExceptionDeleteItem, e.Path],
		ModDeletionException e => strings[DesktopStrings.ExceptionDeleteMod, e.ModName],
		SaveDeletionException e => strings[DesktopStrings.ExceptionDeleteSave, e.FileName],
		LocalSavesDisabledException e => strings[DesktopStrings.ExceptionLocalSavesDisabled, e.Profile],
		DuplicatePluginException e => strings[DesktopStrings.ExceptionDuplicatePlugin, e.PluginName],
		StoredFileInUseException e => strings[DesktopStrings.ExceptionStoredFileInUse, e.Path],
		StoredFileAccessDeniedException e => strings[DesktopStrings.ExceptionStoredFileAccessDenied, e.Path],
		StoredFileDeletionException e => strings[DesktopStrings.ExceptionDeleteItem, e.Path],
		NotEnoughSpaceException e => strings[DesktopStrings.ExceptionNotEnoughSpace, e.DiskName,
			BytesFormatter.Format(e.RequiredSpace, BytesFormatter.Measurement.Size.Instance),
			BytesFormatter.Format(e.AvailableFreeSpace, BytesFormatter.Measurement.Size.Instance)],
		ModpackNotInstalledException => strings[DesktopStrings.ExceptionModpackNotInstalled],
		ModNotSupportedModpackVersionException e => strings[DesktopStrings.ExceptionUnsupportedMod, e.ModName, e.ModpackVersion],
		ModOrganizerAlreadyRunningException => strings[DesktopStrings.ExceptionModOrganizerRunning],
		DownloadServersUnavailableException => strings[DesktopStrings.ExceptionDownloadServers],
		DownloadStalledException e => strings[DesktopStrings.ExceptionDownloadStalled, e.Timeout],
		OAuthAuthorizationException { Error: OAuthAuthorizationException.AccessDeniedError } e => strings[DesktopStrings.ExceptionOAuthCancelled, e.Provider],
		OAuthAuthorizationException e => strings[DesktopStrings.ExceptionOAuth, e.Provider, e.Error],
		SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse } => strings[DesktopStrings.ExceptionOAuthPort],
		_ => strings[DesktopStrings.ExceptionUnknown, exception.Message],
	};
}
