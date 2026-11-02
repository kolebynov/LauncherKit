using FomodInstaller.Scripting.XmlScript;
using Microsoft.Extensions.Logging;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.Fomod;

public sealed class FomodInstaller : IFomodInstaller
{
	private readonly ISevenZipService _sevenZipService;
	private readonly ITempStorageService _tempStorageService;
	private readonly ILogger<FomodInstaller> _logger;
	private readonly IFileSystemUtils _fileSystemUtils;
	private readonly IComparer<string> _pathComparer;
	private readonly XmlScript _fomodScript;
	private readonly string _archivePath;
	private readonly string _modOrganizerDirectory;
	private readonly string _modName;

	public string Title => _fomodScript.HeaderInfo.Title;

	public IEnumerable<InstallStep> InstallSteps => _fomodScript.InstallSteps;

	public FomodInstaller(ISevenZipService sevenZipService, ITempStorageService tempStorageService, ILogger<FomodInstaller> logger,
		IFileSystemUtils fileSystemUtils, IComparer<string> pathComparer, XmlScript fomodScript, string archivePath,
		string modOrganizerDirectory, string modName)
	{
		_sevenZipService = sevenZipService;
		_tempStorageService = tempStorageService;
		_logger = logger;
		_pathComparer = pathComparer;
		_fomodScript = fomodScript;
		_archivePath = archivePath;
		_modOrganizerDirectory = modOrganizerDirectory;
		_modName = modName;
		_fileSystemUtils = fileSystemUtils;
	}

	public async Task Install(IReadOnlyList<ConfiguredInstallStep> configuredInstallSteps, CancellationToken cancellationToken)
	{
		var configuredInstallStepsStr = string.Join(
			'\n',
			configuredInstallSteps.Select(x => $"{x.Step.Name}: {string.Join(',', x.SelectedOptions.Select(y => y.Name))}"));
		_logger.LogInformation("Installing FOMOD.\n{InstalledSteps}", configuredInstallStepsStr);

		using var tempDirectoryHandle = _tempStorageService.CreateTempDirectory();
		var tempDirectory = tempDirectoryHandle.Path;
		var dirsToExtract = GetDirsToExtract(_fomodScript.RequiredInstallFiles)
			.Concat(GetDirsToExtract(configuredInstallSteps
				.SelectMany(step => step.SelectedOptions.SelectMany(option => option.Files))))
			.ToArray();
		_logger.LogInformation("Extracting {Count} directories from archive", dirsToExtract.Length);
		await _sevenZipService.ExtractEntriesFromArchive(dirsToExtract, _archivePath, tempDirectory, true, cancellationToken);

		var modDirectory = Path.Combine(_modOrganizerDirectory, "mods", _modName);
		_fileSystemUtils.SafeDeleteDirectory(modDirectory);

		await Task.Factory.StartNew(
			() => _fileSystemUtils.MoveContents(Directory.EnumerateDirectories(tempDirectory).Order(_pathComparer), modDirectory),
			cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
	}

	private static IEnumerable<string> GetDirsToExtract(IEnumerable<InstallableFile> installableFiles)
	{
		return installableFiles
			.Select(x =>
			{
				if (!x.IsFolder)
				{
					throw new NotSupportedException("Only folders are supported");
				}

				if (!string.IsNullOrEmpty(x.Destination))
				{
					throw new NotSupportedException("Destination is not supported");
				}

				return x.Source;
			});
	}
}
