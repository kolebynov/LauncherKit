using Microsoft.Extensions.Localization;
using LauncherKit.Desktop.ViewModels;
using LauncherKit.Desktop.ViewModels.Setup;
using LauncherKit.Core.Feed;

namespace LauncherKit.Desktop.Localization;

public sealed class SetupAccountFactTexts(IStringLocalizer<DesktopStrings> strings)
{
	public LocalizedString Get(DirectoryValidationError error) => strings[error switch
	{
		DirectoryValidationError.InvalidPath => DesktopStrings.DirectoryInvalidPath,
		DirectoryValidationError.ConflictingDirectory => DesktopStrings.DirectoryConflict,
		DirectoryValidationError.SkyrimDirectoryConflict => DesktopStrings.DirectorySkyrimConflict,
		DirectoryValidationError.AppDataDirectoryConflict => DesktopStrings.DirectoryAppDataConflict,
		DirectoryValidationError.NonAsciiSkyrimPath => DesktopStrings.DirectorySkyrimCharacters,
		_ => throw new ArgumentOutOfRangeException(nameof(error), error, null),
	}];

	public LocalizedString Get(DiscordMembership membership) => strings[membership switch
	{
		DiscordMembership.NotMember => DesktopStrings.DiscordNotMember,
		DiscordMembership.Member => DesktopStrings.DiscordMember,
		DiscordMembership.Sponsor => DesktopStrings.DiscordSponsor,
		DiscordMembership.Developer => DesktopStrings.DiscordDeveloper,
		_ => throw new ArgumentOutOfRangeException(nameof(membership), membership, null),
	}];

	public LocalizedString Get(ActivateBetaCodeResult result) => result switch
	{
		ActivateBetaCodeResult.InvalidBetaCode => strings[DesktopStrings.BetaCodeInvalid],
		ActivateBetaCodeResult.AlreadyActivated => strings[DesktopStrings.BetaCodeAlreadyActivated],
		ActivateBetaCodeResult.Success => strings[DesktopStrings.BetaCodeSuccess],
	};

	public LocalizedString Get(SetupError error) => strings[DesktopStrings.SetupUnsupportedVersion,
		error.DetectedSkyrimVersion, error.RequiredSkyrimVersion];
}
