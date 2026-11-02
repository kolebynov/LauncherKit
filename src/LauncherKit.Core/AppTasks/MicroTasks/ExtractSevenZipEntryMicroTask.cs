using R3;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.AppTasks.MicroTasks;

/// <summary>
/// Extracts the single entry <c>entryPath</c> of a stored archive, keeping its path, when the archive contains it; an
/// archive without it is left alone.
/// </summary>
internal sealed class ExtractSevenZipEntryMicroTask : IAppMicroTask
{
	private readonly IFileStorageService _fileStorageService;
	private readonly ISevenZipService _sevenZipService;
	private readonly string _archiveName;
	private readonly string _destinationDirectory;
	private readonly string _entryPath;

	public MicroTaskDescription Description { get; }

	public ExtractSevenZipEntryMicroTask(IFileStorageService fileStorageService, ISevenZipService sevenZipService,
		MicroTaskDescription description, string archiveName, SkyrimRelativePath destinationPath, string skyrimDirectory, string entryPath)
	{
		_fileStorageService = fileStorageService;
		_sevenZipService = sevenZipService;
		Description = description;
		_archiveName = archiveName;
		_destinationDirectory = destinationPath.ToAbsolute(skyrimDirectory);
		_entryPath = entryPath;
	}

	public async ValueTask Execute(ISubject<MicroTaskStep> stepSubject, CancellationToken cancellationToken)
	{
		var archivePath = _fileStorageService.GetFilePath(_archiveName)!;
		if (await _sevenZipService.ContainsEntry(archivePath, _entryPath, cancellationToken))
		{
			await _sevenZipService.ExtractEntriesFromArchive([_entryPath], archivePath, _destinationDirectory, true,
				cancellationToken);
		}
	}
}
