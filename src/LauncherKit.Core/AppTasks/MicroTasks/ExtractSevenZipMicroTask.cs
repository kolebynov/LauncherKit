using R3;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.AppTasks.MicroTasks;

/// <summary>Extracts a stored archive whole, or without the entry <c>excludedEntryPath</c> when set.</summary>
internal sealed class ExtractSevenZipMicroTask : IAppMicroTask
{
	private readonly IFileStorageService _fileStorageService;
	private readonly ISevenZipService _sevenZipService;
	private readonly string _archiveName;
	private readonly string _destinationDirectory;
	private readonly string? _excludedEntryPath;

	public MicroTaskDescription Description { get; }

	public ExtractSevenZipMicroTask(IFileStorageService fileStorageService, ISevenZipService sevenZipService,
		MicroTaskDescription? description, string archiveName, SkyrimRelativePath destinationPath, string skyrimDirectory,
		string? excludedEntryPath = null)
	{
		_fileStorageService = fileStorageService;
		_sevenZipService = sevenZipService;
		Description = description ?? new MicroTaskDescription.ExtractArchive(archiveName);
		_archiveName = archiveName;
		_destinationDirectory = destinationPath.ToAbsolute(skyrimDirectory);
		_excludedEntryPath = excludedEntryPath;
	}

	public async ValueTask Execute(ISubject<MicroTaskStep> stepSubject, CancellationToken cancellationToken)
	{
		var archivePath = _fileStorageService.GetFilePath(_archiveName)!;
		await (_excludedEntryPath == null
			? _sevenZipService.ExtractArchive(archivePath, _destinationDirectory, cancellationToken)
			: _sevenZipService.ExtractArchiveExcept(archivePath, _destinationDirectory, _excludedEntryPath,
				cancellationToken));
	}
}
