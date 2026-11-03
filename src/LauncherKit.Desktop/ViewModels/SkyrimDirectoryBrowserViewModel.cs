using LauncherKit.Core.Utils;

namespace LauncherKit.Desktop.ViewModels;

public sealed class SkyrimDirectoryBrowserViewModel : DirectoryBrowserViewModel
{
	private readonly ISkyrimInstallationChecker? _installationChecker;

	public SkyrimDirectoryBrowserViewModel() { }

	public SkyrimDirectoryBrowserViewModel(ISkyrimInstallationChecker installationChecker) =>
		_installationChecker = installationChecker;

	public override void SelectDirectory(string directory) => SelectedDirectory =
		_installationChecker != null && _installationChecker.CheckSkyrimDirectory(directory) is not SkyrimDirectoryCheckResult.SkyrimDirectory
			? Path.Combine(directory, "Skyrim") : directory;

	protected override DirectoryValidationError? ValidateSelectedDirectory(string selectedDirectory) =>
		selectedDirectory.All(char.IsAscii) ? null : DirectoryValidationError.NonAsciiSkyrimPath;
}
