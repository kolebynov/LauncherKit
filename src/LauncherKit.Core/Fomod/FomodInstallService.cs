using FomodInstaller.Scripting.XmlScript;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.Fomod;

public sealed class FomodInstallService : IFomodInstallService
{
	private const string ModuleConfigPath = "FOMOD/ModuleConfig.xml";

	private readonly ISevenZipService _sevenZipService;
	private readonly ITempStorageService _tempStorageService;
	private readonly ILogger<FomodInstaller> _logger;
	private readonly IFileSystemUtils _fileSystemUtils;
	private readonly IComparer<string> _pathComparer;
	private readonly string _modOrganizerDirectory;

	public FomodInstallService(IOptionsSnapshot<AppSettings> appSettings, ISevenZipService sevenZipService,
		ITempStorageService tempStorageService, ILogger<FomodInstaller> logger, IFileSystemUtils fileSystemUtils,
		IComparer<string> pathComparer, IOptions<LauncherProductOptions> product)
	{
		_sevenZipService = sevenZipService;
		_tempStorageService = tempStorageService;
		_logger = logger;
		_fileSystemUtils = fileSystemUtils;
		_pathComparer = pathComparer;
		_modOrganizerDirectory = appSettings.Value.GetModOrganizerDirectory(product.Value)!;
	}

	public Task<bool> IsFomodArchive(string archivePath, CancellationToken cancellationToken) =>
		_sevenZipService.ContainsEntry(archivePath, ModuleConfigPath, cancellationToken);

	public async Task<IFomodInstaller> CreateInstaller(string archivePath, string modName, CancellationToken cancellationToken)
	{
		using var tempDirectoryHandle = _tempStorageService.CreateTempDirectory();
		var tempDirectory = tempDirectoryHandle.Path;
		await _sevenZipService.ExtractEntriesFromArchive([ModuleConfigPath], archivePath,
			tempDirectory, false, cancellationToken);
		var moduleConfigData = await File.ReadAllTextAsync(Path.Combine(tempDirectory, "ModuleConfig.xml"), cancellationToken);
		var graphPackScript = new XmlScriptType().LoadScript(moduleConfigData, true);
		return new FomodInstaller(_sevenZipService, _tempStorageService, _logger, _fileSystemUtils, _pathComparer, graphPackScript,
			archivePath, _modOrganizerDirectory, modName);
	}
}
